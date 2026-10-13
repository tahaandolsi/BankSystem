using BankSystem.Shared.Domain.Events;

namespace BankSystem.Shared.Domain.Events.Account;

/// <summary>Emitted when the Account service has applied a completed transaction.</summary>
public sealed record AccountTransactionAppliedIntegrationEvent(
    Guid TransactionId,
    Guid AccountId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string Type,
    decimal BalanceAfter,
    DateTimeOffset AppliedAt,
    Guid? ToAccountId = null) : DomainEvent;
