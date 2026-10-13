using BankSystem.Transaction.Domain.Enums;
using BankSystem.Transaction.Domain.ValueObjects;
namespace BankSystem.Transaction.Domain.Entities;
public sealed class Transaction {
  private Transaction() { }
  public Guid Id { get; private set; }
  public Guid? PaymentId { get; private set; }
  public Guid? AuthorizationId { get; private set; }
  public Guid AccountId { get; private set; }
  public Guid? ToAccountId { get; private set; }
  public decimal Amount { get; private set; }
  public string Currency { get; private set; } = "USD";
  public TransactionType Type { get; private set; }
  public TransactionStatus Status { get; private set; }
  public string Description { get; private set; } = string.Empty;
  public string Reference { get; private set; } = string.Empty;
  public string IdempotencyKey { get; private set; } = string.Empty;
  public DateTime ProcessedAt { get; private set; }
  public string? FailureReason { get; private set; }
  public static Transaction Create(Guid accountId, Guid? toAccountId, Money amount, TransactionType type, string description, string reference, string idempotencyKey, Guid? paymentId = null, Guid? authorizationId = null) {
    if (accountId == Guid.Empty) throw new ArgumentException("AccountId is required");
    if (amount.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
    if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("Idempotency key is required");
    if (paymentId == Guid.Empty) throw new ArgumentException("PaymentId cannot be empty", nameof(paymentId));
    if (authorizationId == Guid.Empty) throw new ArgumentException("AuthorizationId cannot be empty", nameof(authorizationId));
    if (authorizationId is not null && (paymentId is null || toAccountId is null || type != TransactionType.Transfer))
      throw new ArgumentException("AuthorizationId is only valid for a Payment transfer", nameof(authorizationId));
    return new Transaction { Id=Guid.NewGuid(), PaymentId=paymentId, AuthorizationId=authorizationId, AccountId=accountId, ToAccountId=toAccountId, Amount=amount.Amount, Currency=amount.Currency, Type=type, Status=TransactionStatus.Pending, Description=description ?? string.Empty, Reference=reference ?? string.Empty, IdempotencyKey=idempotencyKey };
  }
  public void Complete(DateTimeOffset appliedAt) {
    if (appliedAt == default) throw new ArgumentException("Account application time is required", nameof(appliedAt));
    Status=TransactionStatus.Completed;
    ProcessedAt=appliedAt.UtcDateTime;
  }
  public void Fail(string reason, DateTimeOffset rejectedAt) {
    if (rejectedAt == default) throw new ArgumentException("Account rejection time is required", nameof(rejectedAt));
    Status=TransactionStatus.Failed;
    FailureReason=reason;
    ProcessedAt=rejectedAt.UtcDateTime;
  }
  public void Reverse() { if(Status!=TransactionStatus.Completed) throw new InvalidOperationException("Only completed transactions can be reversed"); Status=TransactionStatus.Reversed; ProcessedAt=DateTime.UtcNow; }
}
