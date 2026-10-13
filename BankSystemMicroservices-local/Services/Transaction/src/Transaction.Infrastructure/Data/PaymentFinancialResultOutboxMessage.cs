namespace BankSystem.Transaction.Infrastructure.Data;

public sealed class PaymentFinancialResultOutboxMessage
{
    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid PaymentId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string RoutingKey { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Pending";
    public int AttemptCount { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public DateTime? LockUntilUtc { get; private set; }
    public string? LastError { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }

    private PaymentFinancialResultOutboxMessage() { }

    public static PaymentFinancialResultOutboxMessage Create(
        Guid transactionId,
        Guid paymentId,
        string eventType,
        string routingKey,
        string payloadJson)
    {
        var now = DateTime.UtcNow;
        return new PaymentFinancialResultOutboxMessage
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            PaymentId = paymentId,
            EventType = eventType,
            RoutingKey = routingKey,
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
