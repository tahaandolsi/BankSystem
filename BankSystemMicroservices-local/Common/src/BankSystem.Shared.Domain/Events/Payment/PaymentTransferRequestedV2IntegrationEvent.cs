using System.Text.Json.Serialization;

namespace BankSystem.Shared.Domain.Events.Payment;

/// <summary>
/// Version 2 Payment transfer request. It includes the durable Account authorization reference.
/// </summary>
public sealed record PaymentTransferRequestedV2IntegrationEvent(
    [property: JsonPropertyName("contractVersion")]
    int ContractVersion,
    [property: JsonPropertyName("paymentId")]
    Guid PaymentId,
    [property: JsonPropertyName("authorizationId")]
    Guid AuthorizationId,
    [property: JsonPropertyName("sourceAccountId")]
    Guid SourceAccountId,
    [property: JsonPropertyName("beneficiaryAccountId")]
    Guid BeneficiaryAccountId,
    [property: JsonPropertyName("amount")]
    decimal Amount,
    [property: JsonPropertyName("currency")]
    string Currency,
    [property: JsonPropertyName("merchantReference")]
    string? MerchantReference,
    [property: JsonPropertyName("requestedAt")]
    DateTimeOffset RequestedAt)
{
    public const int CurrentContractVersion = 2;
}
