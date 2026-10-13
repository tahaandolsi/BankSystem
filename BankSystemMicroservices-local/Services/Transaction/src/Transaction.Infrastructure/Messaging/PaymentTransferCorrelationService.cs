using System.Text.Json;
using BankSystem.Shared.Domain.Events.Payment;
using BankSystem.Shared.Domain.Events.Transaction;
using BankSystem.Transaction.Application.Interfaces;
using BankSystem.Transaction.Domain.Entities;
using BankSystem.Transaction.Domain.Enums;
using BankSystem.Transaction.Domain.ValueObjects;
using BankSystem.Transaction.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BankSystem.Transaction.Infrastructure.Messaging;

/// <summary>Persists Payment-linked transactions and schedules Account processing for authorized V2 transfers.</summary>
public sealed class PaymentTransferCorrelationService(
    TransactionDbContext db,
    ITransactionEventPublisher transactionEventPublisher)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> CreateOrGetAsync(
        PaymentTransferRequestedIntegrationEvent command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(
            command.ContractVersion,
            PaymentTransferRequestedIntegrationEvent.CurrentContractVersion,
            command.PaymentId,
            null,
            command.SourceAccountId,
            command.BeneficiaryAccountId,
            command.Amount,
            command.Currency,
            command.MerchantReference,
            command.RequestedAt);

        return await CreateOrGetAsync(
            command.PaymentId,
            null,
            command.SourceAccountId,
            command.BeneficiaryAccountId,
            command.Amount,
            command.Currency,
            command.MerchantReference,
            cancellationToken);
    }

    public async Task<Guid> CreateOrGetAsync(
        PaymentTransferRequestedV2IntegrationEvent command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(
            command.ContractVersion,
            PaymentTransferRequestedV2IntegrationEvent.CurrentContractVersion,
            command.PaymentId,
            command.AuthorizationId,
            command.SourceAccountId,
            command.BeneficiaryAccountId,
            command.Amount,
            command.Currency,
            command.MerchantReference,
            command.RequestedAt);

        var transactionId = await CreateOrGetAsync(
            command.PaymentId,
            command.AuthorizationId,
            command.SourceAccountId,
            command.BeneficiaryAccountId,
            command.Amount,
            command.Currency,
            command.MerchantReference,
            cancellationToken);

        // Keep the request timestamp separate from the later Account confirmation timestamp.
        await transactionEventPublisher.PublishAsync(
            new TransactionExecutionRequestedIntegrationEvent(
                TransactionExecutionRequestedIntegrationEvent.CurrentContractVersion,
                transactionId,
                command.SourceAccountId,
                command.BeneficiaryAccountId,
                command.Amount,
                command.Currency,
                TransactionType.Transfer.ToString(),
                "Payment transfer",
                command.MerchantReference ?? string.Empty,
                DateTimeOffset.UtcNow,
                command.PaymentId,
                command.AuthorizationId),
            cancellationToken);

        // Persist the Bus Outbox message before the Rabbit command consumer can acknowledge delivery.
        await db.SaveChangesAsync(cancellationToken);
        return transactionId;
    }

    private async Task<Guid> CreateOrGetAsync(
        Guid paymentId,
        Guid? authorizationId,
        Guid sourceAccountId,
        Guid beneficiaryAccountId,
        decimal amount,
        string currency,
        string? merchantReference,
        CancellationToken cancellationToken)
    {

        var existing = await db.Transactions
            .AsNoTracking()
            .SingleOrDefaultAsync(transaction => transaction.PaymentId == paymentId, cancellationToken);

        if (existing is not null)
        {
            if (!MatchesCommand(
                    existing,
                    authorizationId,
                    sourceAccountId,
                    beneficiaryAccountId,
                    amount,
                    currency,
                    merchantReference))
                throw new InvalidOperationException("A Payment was already correlated with different transaction data.");

            await EnsureAcceptanceOutboxExistsAsync(existing.Id, paymentId, cancellationToken);
            return existing.Id;
        }

        var transaction = Transaction.Create(
            sourceAccountId,
            beneficiaryAccountId,
            new Money(amount, currency),
            TransactionType.Transfer,
            "Payment transfer",
            merchantReference ?? string.Empty,
            $"payment:{paymentId:N}",
            paymentId,
            authorizationId);

        var acceptedEvent = CreateAcceptedEvent(paymentId, transaction.Id);

        var acceptanceOutbox = PaymentTransactionAcceptedOutboxMessage.Create(
            paymentId,
            transaction.Id,
            JsonSerializer.Serialize(acceptedEvent, JsonOptions));

        db.Transactions.Add(transaction);
        db.PaymentTransactionAcceptedOutboxMessages.Add(acceptanceOutbox);

        // Transaction and acceptance outbox row are saved atomically.
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // A MassTransit retry can reuse this DbContext, so remove failed Added entities first.
            db.Entry(transaction).State = EntityState.Detached;
            db.Entry(acceptanceOutbox).State = EntityState.Detached;

            if (!IsPaymentIdUniqueViolation(exception))
                throw;

            var concurrentTransaction = await db.Transactions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.PaymentId == paymentId,
                    cancellationToken);

            if (concurrentTransaction is null)
                throw;

            if (!MatchesCommand(
                    concurrentTransaction,
                    authorizationId,
                    sourceAccountId,
                    beneficiaryAccountId,
                    amount,
                    currency,
                    merchantReference))
                throw new InvalidOperationException(
                    "A Payment was concurrently correlated with different transaction data.",
                    exception);

            await EnsureAcceptanceOutboxExistsAsync(concurrentTransaction.Id, paymentId, cancellationToken);
            return concurrentTransaction.Id;
        }

        return transaction.Id;
    }

    private async Task EnsureAcceptanceOutboxExistsAsync(
        Guid transactionId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var existingOutbox = await db.PaymentTransactionAcceptedOutboxMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                message => message.PaymentId == paymentId,
                cancellationToken);

        if (existingOutbox is not null)
        {
            if (existingOutbox.TransactionId != transactionId)
                throw new InvalidOperationException("Payment acceptance is linked to a different transaction.");

            return;
        }

        var acceptedEvent = CreateAcceptedEvent(paymentId, transactionId);
        var acceptanceOutbox = PaymentTransactionAcceptedOutboxMessage.Create(
            paymentId,
            transactionId,
            JsonSerializer.Serialize(acceptedEvent, JsonOptions));
        db.PaymentTransactionAcceptedOutboxMessages.Add(acceptanceOutbox);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            db.Entry(acceptanceOutbox).State = EntityState.Detached;
            if (!IsUniqueViolation(exception))
                throw;

            var concurrentOutbox = await db.PaymentTransactionAcceptedOutboxMessages
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    message => message.PaymentId == paymentId,
                    cancellationToken);

            if (concurrentOutbox?.TransactionId != transactionId)
                throw;
        }
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException
                && postgresException.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return true;
            }
        }

        return false;
    }

    private static PaymentTransactionAcceptedIntegrationEvent CreateAcceptedEvent(
        Guid paymentId,
        Guid transactionId)
    {
        return new PaymentTransactionAcceptedIntegrationEvent(
            PaymentTransactionAcceptedIntegrationEvent.CurrentContractVersion,
            paymentId,
            transactionId,
            paymentId,
            DateTimeOffset.UtcNow);
    }

    private static bool IsPaymentIdUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException
                && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
                && postgresException.ConstraintName == "IX_Transactions_PaymentId")
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateCommand(
        int contractVersion,
        int expectedContractVersion,
        Guid paymentId,
        Guid? authorizationId,
        Guid sourceAccountId,
        Guid beneficiaryAccountId,
        decimal amount,
        string currency,
        string? merchantReference,
        DateTimeOffset requestedAt)
    {
        if (contractVersion != expectedContractVersion)
            throw new InvalidOperationException("Unsupported Payment command version.");

        if (paymentId == Guid.Empty
            || sourceAccountId == Guid.Empty
            || beneficiaryAccountId == Guid.Empty
            || sourceAccountId == beneficiaryAccountId)
            throw new InvalidOperationException("Payment and distinct account identifiers are required.");

        if (expectedContractVersion == PaymentTransferRequestedV2IntegrationEvent.CurrentContractVersion
            && (authorizationId is null || authorizationId == Guid.Empty))
            throw new InvalidOperationException("A Payment transfer authorization is required.");

        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new InvalidOperationException("Payment amount cannot be represented exactly in Transaction.");

        if (string.IsNullOrWhiteSpace(currency)
            || currency.Length != 3
            || currency.Any(character => character is < 'A' or > 'Z'))
            throw new InvalidOperationException("Payment currency must be a three-letter uppercase code.");

        if (merchantReference?.Length > 100 || requestedAt == default)
            throw new InvalidOperationException("Payment reference or request time is invalid.");
    }

    private static bool MatchesCommand(
        Transaction transaction,
        Guid? authorizationId,
        Guid sourceAccountId,
        Guid beneficiaryAccountId,
        decimal amount,
        string currency,
        string? merchantReference)
    {
        return transaction.Type == TransactionType.Transfer
            && transaction.AuthorizationId == authorizationId
            && transaction.AccountId == sourceAccountId
            && transaction.ToAccountId == beneficiaryAccountId
            && transaction.Amount == amount
            && transaction.Currency == currency
            && transaction.Reference == (merchantReference ?? string.Empty);
    }
}
