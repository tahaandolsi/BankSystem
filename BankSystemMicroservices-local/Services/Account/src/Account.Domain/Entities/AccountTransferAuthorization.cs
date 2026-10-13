using BankSystem.Account.Domain.Enums;
using BankSystem.Shared.Domain.Common;
using CurrencyValue = BankSystem.Shared.Domain.ValueObjects.Currency;

namespace BankSystem.Account.Domain.Entities;

public sealed class AccountTransferAuthorization
{
    private const decimal MaximumAmount = 9999999999999999.99m;

    private AccountTransferAuthorization() { }

    public Guid AuthorizationId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid BusinessRequestId { get; private set; }
    public Guid SourceAccountId { get; private set; }
    public Guid BeneficiaryAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public AccountTransferAuthorizationStatus Status { get; private set; }
    public Guid? ConsumedByTransactionId { get; private set; }

    public static AccountTransferAuthorization Create(
        Guid requestedByUserId,
        Guid customerId,
        Guid requestId,
        Guid businessRequestId,
        Guid sourceAccountId,
        Guid beneficiaryAccountId,
        decimal amount,
        string currency,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        if (requestedByUserId == Guid.Empty
            || customerId == Guid.Empty
            || requestId == Guid.Empty
            || businessRequestId == Guid.Empty
            || sourceAccountId == Guid.Empty
            || beneficiaryAccountId == Guid.Empty
            || sourceAccountId == beneficiaryAccountId
            || amount <= 0
            || amount > MaximumAmount
            || !CurrencyValue.IsValidCurrencyCode(currency)
            || expiresAt <= createdAt)
        {
            throw new ArgumentException("The transfer authorization data is invalid.");
        }

        return new AccountTransferAuthorization
        {
            AuthorizationId = Guid.NewGuid(),
            RequestedByUserId = requestedByUserId,
            CustomerId = customerId,
            RequestId = requestId,
            BusinessRequestId = businessRequestId,
            SourceAccountId = sourceAccountId,
            BeneficiaryAccountId = beneficiaryAccountId,
            Amount = amount,
            Currency = currency,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            Status = AccountTransferAuthorizationStatus.Active
        };
    }

    public bool MatchesRequest(
        Guid requestedByUserId,
        Guid customerId,
        Guid businessRequestId,
        Guid sourceAccountId,
        Guid beneficiaryAccountId,
        decimal amount,
        string currency)
    {
        return RequestedByUserId == requestedByUserId
            && CustomerId == customerId
            && BusinessRequestId == businessRequestId
            && SourceAccountId == sourceAccountId
            && BeneficiaryAccountId == beneficiaryAccountId
            && Amount == amount
            && Currency == currency;
    }

    public bool IsActiveAt(DateTimeOffset now)
    {
        return Status == AccountTransferAuthorizationStatus.Active && ExpiresAt > now;
    }

    public bool ExpireIfNeeded(DateTimeOffset now)
    {
        if (Status != AccountTransferAuthorizationStatus.Active || ExpiresAt > now)
            return false;

        Status = AccountTransferAuthorizationStatus.Expired;
        return true;
    }

    public string GetStatusAt(DateTimeOffset now)
    {
        if (Status == AccountTransferAuthorizationStatus.Active && ExpiresAt <= now)
            return "Expired";

        return Status.ToString();
    }

    public Result ValidateForExecution(
        Guid businessRequestId,
        Guid transactionId,
        Account sourceAccount,
        Account beneficiaryAccount,
        decimal amount,
        CurrencyValue currency,
        DateTimeOffset now)
    {
        if (Status != AccountTransferAuthorizationStatus.Active || ExpiresAt <= now)
            return Result.Failure("The authorization is not active.", ErrorType.Conflict);

        if (transactionId == Guid.Empty
            || businessRequestId != BusinessRequestId
            || sourceAccount is null
            || beneficiaryAccount is null
            || sourceAccount.Id != SourceAccountId
            || beneficiaryAccount.Id != BeneficiaryAccountId
            || amount != Amount
            || currency is null
            || currency.Code != Currency)
        {
            return Result.Failure("The operation does not match its authorization.", ErrorType.Conflict);
        }

        if (sourceAccount.CustomerId != CustomerId)
            return Result.Failure("The source account owner does not match the authorization.", ErrorType.Conflict);

        if (sourceAccount.Status != AccountStatus.Active
            || beneficiaryAccount.Status != AccountStatus.Active)
        {
            return Result.Failure("Both accounts must be active.", ErrorType.Conflict);
        }

        if (sourceAccount.Balance.Currency.Code != Currency
            || beneficiaryAccount.Balance.Currency.Code != Currency)
        {
            return Result.Failure("The account currency does not match the authorization.", ErrorType.Conflict);
        }

        return Result.Success();
    }

    public Result MarkConsumed(Guid transactionId, DateTimeOffset now)
    {
        if (transactionId == Guid.Empty)
            return Result.Failure("A transaction identifier is required.", ErrorType.Validation);

        if (!IsActiveAt(now) || ConsumedByTransactionId is not null)
        {
            return Result.Failure("The authorization is expired or cannot be consumed again.", ErrorType.Conflict);
        }

        Status = AccountTransferAuthorizationStatus.Consumed;
        ConsumedByTransactionId = transactionId;
        return Result.Success();
    }

    public Result Revoke()
    {
        if (Status != AccountTransferAuthorizationStatus.Active)
            return Result.Failure("Only an active authorization can be revoked.", ErrorType.Conflict);

        Status = AccountTransferAuthorizationStatus.Revoked;
        return Result.Success();
    }
}
