using BankSystem.Shared.Domain.Events;

namespace BankSystem.Shared.Domain.Events.Transaction;

/// <summary>Legacy execution request kept for messages already in circulation.</summary>
public sealed record TransactionCompletedIntegrationEvent(
    Guid TransactionId,
    Guid AccountId,
    Guid? ToAccountId,
    decimal Amount,
    string Currency,
    string Type,
    string Description,
    string Reference,
    DateTimeOffset CompletedAt,
    int WorkflowVersion = 0,
    Guid? PaymentId = null,
    Guid? AuthorizationId = null) : DomainEvent
{
    public const int CurrentWorkflowVersion = 3;
}
