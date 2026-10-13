using System.Text.Json.Serialization;

namespace BankSystem.Shared.Domain.Events.Payment;

/// <summary>
/// Versioned JSON body for a Payment transfer request. It contains no user token.
/// </summary>
public sealed record PaymentTransferRequestedIntegrationEvent(
    [property: JsonPropertyName("contractVersion")]
    int ContractVersion,
    [property: JsonPropertyName("paymentId")]
    Guid PaymentId,
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
    public const int CurrentContractVersion = 1;
}
