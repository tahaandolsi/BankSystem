namespace BankSystem.Account.Infrastructure.Data;

/// <summary>
/// Durable business outcome and idempotency marker for an Account transaction.
/// </summary>
public sealed class AccountTransactionReceipt
{
    public Guid TransactionId { get; set; }

    public AccountTransactionReceiptOutcome Outcome { get; set; }

    public string? RejectionCode { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }

    // Kept for compatibility with the existing consumer during the transition.
    public DateTimeOffset AppliedAt { get; set; }
}

public enum AccountTransactionReceiptOutcome
{
    Applied,
    Rejected
}
