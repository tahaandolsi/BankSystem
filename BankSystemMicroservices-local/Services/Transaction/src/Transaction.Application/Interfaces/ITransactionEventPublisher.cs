using BankSystem.Shared.Domain.Events.Transaction;
namespace BankSystem.Transaction.Application.Interfaces;
public interface ITransactionEventPublisher
{
    Task PublishAsync(TransactionExecutionRequestedIntegrationEvent @event, CancellationToken ct);
    Task PublishAsync(TransactionCompletedIntegrationEvent @event, CancellationToken ct);
}
