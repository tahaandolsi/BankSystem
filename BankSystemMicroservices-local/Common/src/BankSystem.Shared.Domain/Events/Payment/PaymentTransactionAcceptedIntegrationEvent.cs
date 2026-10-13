using System.Text.Json.Serialization;

namespace BankSystem.Shared.Domain.Events.Payment;

/// <summary>Confirms that Transaction created a Payment-linked transaction, not that funds were applied.</summary>
public sealed record PaymentTransactionAcceptedIntegrationEvent(
    [property: JsonPropertyName("contractVersion")]
    int ContractVersion,
    [property: JsonPropertyName("paymentId")]
    Guid PaymentId,
    [property: JsonPropertyName("transactionId")]
    Guid TransactionId,
    [property: JsonPropertyName("correlationId")]
    Guid CorrelationId,
    [property: JsonPropertyName("acceptedAt")]
    DateTimeOffset AcceptedAt)
{
    public const int CurrentContractVersion = 1;
}
