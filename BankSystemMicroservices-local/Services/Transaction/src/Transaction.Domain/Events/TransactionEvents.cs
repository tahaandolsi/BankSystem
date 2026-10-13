using BankSystem.Shared.Domain.Events;
using BankSystem.Transaction.Domain.Enums;
namespace BankSystem.Transaction.Domain.Events;
public record TransactionCreatedEvent(Guid TransactionId, Guid AccountId, Guid? ToAccountId, decimal Amount, string Currency, TransactionType Type, string Reference, DateTime Timestamp) : DomainEvent;
public record TransactionCompletedEvent(Guid TransactionId, Guid AccountId, Guid? ToAccountId, decimal Amount, string Currency, TransactionType Type, string Description, DateTime CompletedAt) : DomainEvent;
public record TransactionFailedEvent(Guid TransactionId, Guid AccountId, decimal Amount, string Reason, DateTime FailedAt) : DomainEvent;
