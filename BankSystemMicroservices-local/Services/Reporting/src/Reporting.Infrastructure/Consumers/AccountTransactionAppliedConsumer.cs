using BankSystem.Reporting.Domain.Entities;
using BankSystem.Reporting.Infrastructure.Data;
using BankSystem.Shared.Domain.Events.Account;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace BankSystem.Reporting.Infrastructure.Consumers;

public sealed class AccountTransactionAppliedConsumer(ReportingDbContext db) : IConsumer<AccountTransactionAppliedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<AccountTransactionAppliedIntegrationEvent> context)
    {
        var message = context.Message;
        if (await db.TransactionAnalytics.AnyAsync(x => x.TransactionId == message.TransactionId, context.CancellationToken)) return;
        db.TransactionAnalytics.Add(new TransactionAnalytics { Id = Guid.NewGuid(), TransactionId = message.TransactionId, AccountId = message.AccountId, CustomerId = message.CustomerId, Amount = message.Amount, Currency = message.Currency, Type = message.Type, CompletedAt = message.AppliedAt.UtcDateTime });
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
