using BankSystem.Shared.Domain.Events;

namespace BankSystem.Shared.Domain.Events.Account;

/// <summary>Emitted when Account definitively rejects a financial transaction.</summary>
public sealed record AccountTransactionRejectedIntegrationEvent(
    Guid TransactionId,
    Guid AccountId,
    Guid? ToAccountId,
    decimal Amount,
    string Currency,
    string Type,
    string RejectionCode,
    DateTimeOffset RejectedAt) : DomainEvent
{
    public const string InvalidAmount = "INVALID_AMOUNT";
    public const string InvalidCurrency = "INVALID_CURRENCY";
    public const string UnsupportedTransactionType = "UNSUPPORTED_TRANSACTION_TYPE";
    public const string InvalidTransfer = "INVALID_TRANSFER";
    public const string AccountNotFound = "ACCOUNT_NOT_FOUND";
    public const string AccountInactive = "ACCOUNT_INACTIVE";
    public const string CurrencyMismatch = "CURRENCY_MISMATCH";
    public const string InsufficientFunds = "INSUFFICIENT_FUNDS";
    public const string TransferAuthorizationRequired = "TRANSFER_AUTHORIZATION_REQUIRED";
    public const string TransferAuthorizationInvalid = "TRANSFER_AUTHORIZATION_INVALID";
}
