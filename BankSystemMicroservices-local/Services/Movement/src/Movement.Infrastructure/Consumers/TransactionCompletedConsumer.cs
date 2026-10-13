using BankSystem.Shared.Domain.Events.Transaction;
using MassTransit;

namespace BankSystem.Movement.Infrastructure.Consumers;

public sealed class TransactionCompletedConsumer : IConsumer<TransactionCompletedIntegrationEvent>
{
    // This legacy event is emitted while the transaction is still Pending.
    // Consume old and new deliveries without creating an unconfirmed movement.
    public Task Consume(ConsumeContext<TransactionCompletedIntegrationEvent> context) => Task.CompletedTask;
}
