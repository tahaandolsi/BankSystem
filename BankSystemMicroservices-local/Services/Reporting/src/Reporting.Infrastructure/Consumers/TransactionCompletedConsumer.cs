using BankSystem.Reporting.Domain.Entities;
using BankSystem.Reporting.Infrastructure.Data;
using BankSystem.Shared.Domain.Events.Transaction;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace BankSystem.Reporting.Infrastructure.Consumers;

public sealed class TransactionCompletedConsumer(ReportingDbContext db) : IConsumer<TransactionCompletedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<TransactionCompletedIntegrationEvent> context)
    {
        var message = context.Message;
        if (await db.TransactionAnalytics.AnyAsync(x => x.TransactionId == message.TransactionId, context.CancellationToken))
            return;
        db.TransactionAnalytics.Add(new TransactionAnalytics { Id = Guid.NewGuid(), TransactionId = message.TransactionId,
            AccountId = message.AccountId, Amount = message.Amount, Currency = message.Currency,
            Type = message.Type, CompletedAt = message.CompletedAt.UtcDateTime });
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
