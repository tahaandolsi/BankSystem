namespace BankSystem.Transaction.Infrastructure.Data;

public sealed class PaymentTransactionAcceptedOutboxMessage
{
    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid TransactionId { get; private set; }
    public string PayloadJson { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Pending";
    public int AttemptCount { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public DateTime? LockUntilUtc { get; private set; }
    public string? LastError { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }

    private PaymentTransactionAcceptedOutboxMessage() { }

    public static PaymentTransactionAcceptedOutboxMessage Create(
        Guid paymentId,
        Guid transactionId,
        string payloadJson)
    {
        var now = DateTime.UtcNow;
        return new PaymentTransactionAcceptedOutboxMessage
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            TransactionId = transactionId,
            PayloadJson = payloadJson,
            Status = "Pending",
            NextAttemptAtUtc = now,
            CreatedAtUtc = now
        };
    }

    public void Claim(DateTime lockUntilUtc)
    {
        Status = "Processing";
        LockUntilUtc = lockUntilUtc;
    }

    public void MarkPublished(DateTime publishedAtUtc)
    {
        Status = "Published";
        PublishedAtUtc = publishedAtUtc;
        LockUntilUtc = null;
        LastError = null;
    }

    public void RecordFailure(string errorType, DateTime failedAtUtc)
    {
        AttemptCount++;
        LastError = errorType.Length > 1000 ? errorType[..1000] : errorType;
        LockUntilUtc = null;

        if (AttemptCount >= 10)
        {
            Status = "Dead";
            return;
        }

        Status = "Pending";
        var delaySeconds = Math.Min(1 << Math.Min(AttemptCount, 12), 3600);
        NextAttemptAtUtc = failedAtUtc.AddSeconds(delaySeconds);
    }
}
