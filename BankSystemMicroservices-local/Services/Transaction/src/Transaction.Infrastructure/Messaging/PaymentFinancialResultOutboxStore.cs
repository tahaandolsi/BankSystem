using BankSystem.Shared.Domain.Events.Payment;
using BankSystem.Transaction.Domain.Entities;
using BankSystem.Transaction.Infrastructure.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BankSystem.Transaction.Infrastructure.Messaging;

public sealed class PaymentFinancialResultOutboxStore(
    TransactionDbContext db,
    bool resultPublishingEnabled)
{
    private const int BatchSize = 10;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PaymentFinancialResultOutboxMessage>> ClaimBatch(
        CancellationToken cancellationToken)
    {
        var now = TruncateToMicroseconds(DateTime.UtcNow);
        var lockUntil = now.Add(LeaseDuration);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var messages = await db.PaymentFinancialResultOutboxMessages
            .FromSqlInterpolated($"""
                SELECT * FROM "PaymentFinancialResultOutboxMessages"
                WHERE ("Status" = 'Pending' AND "NextAttemptAtUtc" <= {now})
                   OR ("Status" = 'Processing' AND "LockUntilUtc" <= {now})
                ORDER BY "CreatedAtUtc"
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
            message.Claim(lockUntil);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages;
    }

    public async Task MarkPublished(
        Guid messageId,
        DateTime? lockUntilUtc,
        CancellationToken cancellationToken)
    {
        var message = await FindActiveClaim(messageId, lockUntilUtc, cancellationToken);
        if (message is null)
            return;

        message.MarkPublished(TruncateToMicroseconds(DateTime.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordFailure(
        Guid messageId,
        DateTime? lockUntilUtc,
        string errorType,
        CancellationToken cancellationToken)
    {
        var message = await FindActiveClaim(messageId, lockUntilUtc, cancellationToken);
        if (message is null)
            return;

        message.RecordFailure(errorType, TruncateToMicroseconds(DateTime.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
    }

    public void AddCompleted(Transaction transaction)
    {
        if (transaction.PaymentId is not Guid paymentId)
            return;

        EnsurePublishingEnabled();
        if (paymentId == Guid.Empty || transaction.ToAccountId is not Guid beneficiaryAccountId)
            throw new InvalidOperationException("Payment-linked transactions require a beneficiary account.");

        var message = new PaymentTransactionCompletedIntegrationEvent(
            PaymentTransactionCompletedIntegrationEvent.CurrentContractVersion,
            paymentId,
            transaction.Id,
            transaction.AccountId,
            beneficiaryAccountId,
            transaction.Amount,
            transaction.Currency,
            ToUtcOffset(transaction.ProcessedAt));

        AddMessage(transaction, paymentId, "Completed", "bank.payment.completed.v1", message);
    }

    public void AddRejected(Transaction transaction)
    {
        if (transaction.PaymentId is not Guid paymentId)
            return;

        EnsurePublishingEnabled();
        if (paymentId == Guid.Empty
            || transaction.ToAccountId is not Guid beneficiaryAccountId
            || string.IsNullOrWhiteSpace(transaction.FailureReason))
            throw new InvalidOperationException("Payment-linked rejection details are incomplete.");

        var message = new PaymentTransactionRejectedIntegrationEvent(
            PaymentTransactionRejectedIntegrationEvent.CurrentContractVersion,
            paymentId,
            transaction.Id,
            transaction.AccountId,
            beneficiaryAccountId,
            transaction.Amount,
            transaction.Currency,
            transaction.FailureReason,
            ToUtcOffset(transaction.ProcessedAt));

        AddMessage(transaction, paymentId, "Rejected", "bank.payment.rejected.v1", message);
    }

    private void AddMessage<TMessage>(
        Transaction transaction,
        Guid paymentId,
        string eventType,
        string routingKey,
        TMessage message)
    {
        var payload = JsonSerializer.Serialize(message, JsonOptions);
        db.PaymentFinancialResultOutboxMessages.Add(
            PaymentFinancialResultOutboxMessage.Create(
                transaction.Id,
                paymentId,
                eventType,
                routingKey,
                payload));
    }

    private static DateTimeOffset ToUtcOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private Task<PaymentFinancialResultOutboxMessage?> FindActiveClaim(
        Guid messageId,
        DateTime? lockUntilUtc,
        CancellationToken cancellationToken)
    {
        if (lockUntilUtc is null)
            return Task.FromResult<PaymentFinancialResultOutboxMessage?>(null);

        return db.PaymentFinancialResultOutboxMessages.SingleOrDefaultAsync(
            message => message.Id == messageId
                && message.Status == "Processing"
                && message.LockUntilUtc == lockUntilUtc,
            cancellationToken);
    }

    private static DateTime TruncateToMicroseconds(DateTime value)
    {
        var ticks = value.Ticks - value.Ticks % 10;
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private void EnsurePublishingEnabled()
    {
        if (!resultPublishingEnabled)
            throw new InvalidOperationException(
                "Payment result publishing is disabled; the transaction result was not committed.");
    }
}
