using System.Text.Json.Serialization;

namespace BankSystem.Shared.Domain.Events.Payment;

/// <summary>Payment-specific result emitted after Account confirms the transfer.</summary>
public sealed record PaymentTransactionCompletedIntegrationEvent(
    [property: JsonPropertyName("contractVersion")]
    int ContractVersion,
    [property: JsonPropertyName("paymentId")]
    Guid PaymentId,
    [property: JsonPropertyName("transactionId")]
    Guid TransactionId,
    [property: JsonPropertyName("sourceAccountId")]
    Guid SourceAccountId,
    [property: JsonPropertyName("beneficiaryAccountId")]
    Guid BeneficiaryAccountId,
    [property: JsonPropertyName("amount")]
    decimal Amount,
    [property: JsonPropertyName("currency")]
    string Currency,
    [property: JsonPropertyName("completedAt")]
    DateTimeOffset CompletedAt)
{
    public const int CurrentContractVersion = 1;
}
