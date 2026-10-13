using BankSystem.Shared.Domain.Events.Account;
using BankSystem.Shared.Domain.Events.Transaction;
using BankSystem.Transaction.Domain.Entities;
using BankSystem.Transaction.Domain.Enums;
using BankSystem.Transaction.Infrastructure.Data;
using BankSystem.Transaction.Infrastructure.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BankSystem.Transaction.Infrastructure.Consumers;

public sealed class AccountTransactionAppliedConsumer(
    TransactionDbContext db,
    PaymentFinancialResultOutboxStore paymentResultOutbox,
    ILogger<AccountTransactionAppliedConsumer> logger)
    : IConsumer<AccountTransactionAppliedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<AccountTransactionAppliedIntegrationEvent> context)
    {
        var message = context.Message;

        // A retry can reuse the scoped DbContext. Detach a previously tracked copy so
        // the locked query reads the current persisted transaction status.
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
            logger.LogError(
                "Account confirmed unknown transaction {TransactionId}",
                message.TransactionId);
            throw new InvalidOperationException(
                $"Account confirmed unknown transaction {message.TransactionId}.");
        }

        if (transaction.AccountId != message.AccountId
            || transaction.ToAccountId != message.ToAccountId
            || transaction.Amount != message.Amount
            || transaction.Currency != message.Currency
            || transaction.Type.ToString() != message.Type
            || message.AppliedAt == default)
        {
            logger.LogError(
                "Account confirmation does not match transaction {TransactionId}",
                message.TransactionId);
            throw new InvalidOperationException(
                $"Account confirmation does not match transaction {message.TransactionId}.");
        }

        if (transaction.Status == TransactionStatus.Completed)
            return;

        if (transaction.Status != TransactionStatus.Pending)
        {
            logger.LogError(
                "Account confirmed transaction {TransactionId} in unexpected status {Status}",
                transaction.Id,
                transaction.Status);
            throw new InvalidOperationException(
                $"Transaction {transaction.Id} cannot be completed from status {transaction.Status}.");
        }

        transaction.Complete(message.AppliedAt);
        paymentResultOutbox.AddCompleted(transaction);
        await db.SaveChangesAsync(context.CancellationToken);

        await context.Publish(
            new TransactionFinanciallyCompletedIntegrationEvent(
                transaction.Id,
                transaction.AccountId,
                transaction.ToAccountId,
                transaction.Amount,
                transaction.Currency,
                transaction.Type.ToString(),
                transaction.Description,
                transaction.Reference,
                new DateTimeOffset(DateTime.SpecifyKind(transaction.ProcessedAt, DateTimeKind.Utc))),
            context.CancellationToken);
    }
}
