using System.Text.Json.Serialization;
using BankSystem.Shared.Domain.Events;

namespace BankSystem.Shared.Domain.Events.Transaction;

/// <summary>Requests financial execution; it is not a confirmation that funds were applied.</summary>
public sealed record TransactionExecutionRequestedIntegrationEvent(
    [property: JsonPropertyName("contractVersion")]
    int ContractVersion,
    [property: JsonPropertyName("transactionId")]
    Guid TransactionId,
    [property: JsonPropertyName("accountId")]
    Guid AccountId,
    [property: JsonPropertyName("toAccountId")]
    Guid? ToAccountId,
    [property: JsonPropertyName("amount")]
    decimal Amount,
    [property: JsonPropertyName("currency")]
    string Currency,
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("description")]
    string Description,
    [property: JsonPropertyName("reference")]
    string Reference,
    [property: JsonPropertyName("requestedAt")]
    DateTimeOffset RequestedAt,
    [property: JsonPropertyName("paymentId")]
    Guid? PaymentId = null,
    [property: JsonPropertyName("authorizationId")]
    Guid? AuthorizationId = null) : DomainEvent
{
    public const int CurrentContractVersion = 1;
}
