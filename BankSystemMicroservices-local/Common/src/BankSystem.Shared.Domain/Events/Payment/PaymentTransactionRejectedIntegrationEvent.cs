using System.Text.Json.Serialization;

namespace BankSystem.Shared.Domain.Events.Payment;

/// <summary>Payment-specific result emitted after a definitive business rejection.</summary>
public sealed record PaymentTransactionRejectedIntegrationEvent(
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
    [property: JsonPropertyName("rejectionCode")]
    string RejectionCode,
    [property: JsonPropertyName("rejectedAt")]
    DateTimeOffset RejectedAt)
{
    public const int CurrentContractVersion = 1;
}
