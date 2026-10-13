namespace BankSystem.Reporting.Domain.Entities;

/// <summary>Immutable local projection used by transaction and compliance reports.</summary>
public sealed class TransactionAnalytics
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public Guid AccountId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
}
