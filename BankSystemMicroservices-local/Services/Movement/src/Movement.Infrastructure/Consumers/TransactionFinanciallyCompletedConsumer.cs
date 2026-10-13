using BankSystem.Movement.Infrastructure.Data;
using BankSystem.Shared.Domain.Events.Transaction;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using MovementEntity = BankSystem.Movement.Domain.Entities.Movement;

namespace BankSystem.Movement.Infrastructure.Consumers;

public sealed class TransactionFinanciallyCompletedConsumer(MovementDbContext db)
    : IConsumer<TransactionFinanciallyCompletedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<TransactionFinanciallyCompletedIntegrationEvent> context)
    {
        var message = context.Message;
        var movementExists = await db.Movements.AnyAsync(
            movement => movement.TransactionId == message.TransactionId,
            context.CancellationToken);

        if (movementExists)
            return;

        db.Movements.Add(new MovementEntity
        {
            Id = Guid.NewGuid(),
            TransactionId = message.TransactionId,
            AccountId = message.AccountId,
            ToAccountId = message.ToAccountId,
            Amount = message.Amount,
            Currency = message.Currency,
            Type = message.Type,
            Description = message.Description,
            Reference = message.Reference,
            Timestamp = message.CompletedAt.UtcDateTime
        });

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
