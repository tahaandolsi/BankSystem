using BankSystem.Shared.Domain.Events;

namespace BankSystem.Shared.Domain.Events.Transaction;

/// <summary>Emitted after Account confirms that the transaction was financially applied.</summary>
public sealed record TransactionFinanciallyCompletedIntegrationEvent(
    Guid TransactionId,
    Guid AccountId,
    Guid? ToAccountId,
    decimal Amount,
    string Currency,
    string Type,
    string Description,
    string Reference,
    DateTimeOffset CompletedAt) : DomainEvent;
