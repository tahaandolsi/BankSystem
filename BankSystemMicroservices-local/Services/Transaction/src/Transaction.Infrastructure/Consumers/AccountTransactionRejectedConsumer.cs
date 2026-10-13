using BankSystem.Shared.Domain.Events.Account;
using BankSystem.Transaction.Domain.Entities;
using BankSystem.Transaction.Domain.Enums;
using BankSystem.Transaction.Infrastructure.Data;
using BankSystem.Transaction.Infrastructure.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BankSystem.Transaction.Infrastructure.Consumers;

public sealed class AccountTransactionRejectedConsumer(
    TransactionDbContext db,
    PaymentFinancialResultOutboxStore paymentResultOutbox,
    ILogger<AccountTransactionRejectedConsumer> logger)
    : IConsumer<AccountTransactionRejectedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<AccountTransactionRejectedIntegrationEvent> context)
    {
        var message = context.Message;

        var trackedTransaction = db.ChangeTracker
            .Entries<Transaction>()
            .FirstOrDefault(entry => entry.Entity.Id == message.TransactionId);
        if (trackedTransaction is not null)
            trackedTransaction.State = EntityState.Detached;

        var transaction = await db.Transactions
            .FromSqlInterpolated($"SELECT * FROM \"Transactions\" WHERE \"Id\" = {message.TransactionId} FOR UPDATE")
            .SingleOrDefaultAsync(context.CancellationToken);

        if (transaction is null)
        {
            logger.LogError("Account rejected unknown transaction {TransactionId}", message.TransactionId);
            throw new InvalidOperationException($"Account rejected unknown transaction {message.TransactionId}.");
        }

        if (!IsConsistentRejection(message, transaction))
        {
            logger.LogError("Account rejection does not match transaction {TransactionId}", message.TransactionId);
            throw new InvalidOperationException($"Account rejection does not match transaction {message.TransactionId}.");
        }

        if (transaction.Status == TransactionStatus.Failed)
            return;

        if (transaction.Status == TransactionStatus.Completed)
        {
            logger.LogError("Account rejected already completed transaction {TransactionId}", transaction.Id);
            throw new InvalidOperationException($"Completed transaction {transaction.Id} cannot be rejected.");
        }

        if (transaction.Status != TransactionStatus.Pending)
        {
            logger.LogError(
                "Account rejected transaction {TransactionId} in unexpected status {Status}",
                transaction.Id,
                transaction.Status);
            throw new InvalidOperationException(
                $"Transaction {transaction.Id} cannot be failed from status {transaction.Status}.");
        }

        transaction.Fail(message.RejectionCode, message.RejectedAt);
        paymentResultOutbox.AddRejected(transaction);
        await db.SaveChangesAsync(context.CancellationToken);
    }

    private static bool IsConsistentRejection(
        AccountTransactionRejectedIntegrationEvent message,
        Transaction transaction)
    {
        return message.AccountId == transaction.AccountId
            && message.ToAccountId == transaction.ToAccountId
            && message.Amount == transaction.Amount
            && message.Currency == transaction.Currency
            && message.Type == transaction.Type.ToString()
            && message.RejectedAt != default
            && IsKnownRejectionCode(message.RejectionCode);
    }

    private static bool IsKnownRejectionCode(string rejectionCode)
    {
        return rejectionCode is AccountTransactionRejectedIntegrationEvent.InvalidAmount
            or AccountTransactionRejectedIntegrationEvent.InvalidCurrency
            or AccountTransactionRejectedIntegrationEvent.UnsupportedTransactionType
            or AccountTransactionRejectedIntegrationEvent.InvalidTransfer
            or AccountTransactionRejectedIntegrationEvent.AccountNotFound
            or AccountTransactionRejectedIntegrationEvent.AccountInactive
            or AccountTransactionRejectedIntegrationEvent.CurrencyMismatch
            or AccountTransactionRejectedIntegrationEvent.InsufficientFunds
            or AccountTransactionRejectedIntegrationEvent.TransferAuthorizationRequired
            or AccountTransactionRejectedIntegrationEvent.TransferAuthorizationInvalid;
    }
}
