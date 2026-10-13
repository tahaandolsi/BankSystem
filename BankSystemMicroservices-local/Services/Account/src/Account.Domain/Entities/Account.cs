using BankSystem.Account.Domain.Enums;
using BankSystem.Shared.Domain.Common;
using BankSystem.Shared.Domain.Events.Account;
using BankSystem.Shared.Domain.Validation;
using BankSystem.Shared.Domain.ValueObjects;

namespace BankSystem.Account.Domain.Entities;

/// <summary>
/// Represents a bank account aggregate root in the banking domain.
/// An account is the primary entity for managing customer finances and transactions.
/// </summary>
public class Account : AggregateRoot<Guid>
{
    private const string InvalidAmountErrorCode = "INVALID_AMOUNT";
    private const string InvalidCurrencyErrorCode = "INVALID_CURRENCY";
    private const string UnsupportedTransactionTypeErrorCode = "UNSUPPORTED_TRANSACTION_TYPE";
    private const string InvalidTransferErrorCode = "INVALID_TRANSFER";
    private const string AccountInactiveErrorCode = "ACCOUNT_INACTIVE";
    private const string CurrencyMismatchErrorCode = "CURRENCY_MISMATCH";
    private const string InsufficientFundsErrorCode = "INSUFFICIENT_FUNDS";

    /// <summary>
    /// Gets the unique account number for this account.
    /// </summary>
    public AccountNumber AccountNumber { get; } = null!;

    /// <summary>
    /// Gets the customer who owns this account.
    /// </summary>
    public Guid CustomerId { get; }

    /// <summary>
    /// Gets the current balance of the account.
    /// </summary>
    public Money Balance { get; private set; } = null!;

    /// <summary>
    /// Gets the current status of the account.
    /// </summary>
    public AccountStatus Status { get; private set; }

    /// <summary>
    /// Gets the type of account (Checking, Savings, etc.).
    /// </summary>
    public AccountType Type { get; private set; }

    /// <summary>
    /// Gets the date and time when the account was closed (if applicable).
    /// </summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    // Private constructor for EF Core
    private Account() { }

    /// <summary>
    /// Initializes a new instance of the Account class.
    /// </summary>
    /// <param name="accountNumber">The unique account number.</param>
    /// <param name="customerId">The customer ID who owns the account.</param>
    /// <param name="type">The type of account.</param>
    /// <param name="currency">The currency for the account.</param>
    private Account(
        AccountNumber accountNumber,
        Guid customerId,
        AccountType type,
        Currency currency
    )
    {
        Guard.AgainstNullOrEmpty(accountNumber);
        Guard.AgainstNullOrEmpty(currency);
        Guard.AgainstEmptyGuid(customerId);

        Id = Guid.NewGuid();
        AccountNumber = accountNumber;
        CustomerId = customerId;
        Type = type;
        Balance = Money.Zero(currency);
        Status = AccountStatus.PendingActivation;
    }

    /// <summary>
    /// Creates a new account for a customer.
    /// </summary>
    /// <param name="customerId">The customer ID who will own the account.</param>
    /// <param name="type">The type of account to create.</param>
    /// <param name="currency">The currency for the account.</param>
    /// <returns>A new account instance.</returns>
    public static Account CreateNew(Guid customerId, AccountType type, Currency currency, string countryCode = "BE")
    {
        var accountNumber = AccountNumber.Generate(countryCode);
        var account = new Account(accountNumber, customerId, type, currency);

        account.AddDomainEvent(
            new AccountCreatedEvent(
                account.Id,
                customerId,
                account.AccountNumber,
                type.ToString(),
                DateTimeOffset.UtcNow
            )
        );

        return account;
    }

    /// <summary>
    /// Activates the account, allowing transactions to be processed.
    /// </summary>
    public Result Activate()
    {
        if (Status == AccountStatus.Active)
            return Result.Failure("Account is already active");

        if (Status == AccountStatus.Closed)
            return Result.Failure("Cannot activate a closed account");

        Status = AccountStatus.Active;

        AddDomainEvent(
            new AccountActivatedEvent(Id, CustomerId, AccountNumber.Value, DateTimeOffset.UtcNow)
        );
        return Result.Success();
    }

    /// <summary>
    /// Suspends the account, preventing new transactions.
    /// </summary>
    /// <param name="reason">The reason for suspension.</param>
    public Result Suspend(string reason)
    {
        Guard.AgainstNullOrEmpty(reason);

        if (Status == AccountStatus.Closed)
            return Result.Failure("Cannot suspend a closed account");

        Status = AccountStatus.Suspended;

        AddDomainEvent(new AccountSuspendedEvent(Id, reason, DateTimeOffset.UtcNow));
        return Result.Success();
    }

    /// <summary>
    /// Freezes the account.
    /// </summary>
    /// <param name="reason">The reason for freezing the account.</param>
    public Result Freeze(string reason)
    {
        Guard.AgainstNullOrEmpty(reason);

        if (Status == AccountStatus.Frozen)
            return Result.Failure("Account is already frozen");

        if (Status == AccountStatus.Closed)
            return Result.Failure("Cannot freeze a closed account");

        Status = AccountStatus.Frozen;

        AddDomainEvent(new AccountFrozenEvent(Id, AccountNumber, CustomerId, reason));
        return Result.Success();
    }

    /// <summary>
    /// Closes the account permanently.
    /// </summary>
    /// <param name="reason">The reason for closing the account.</param>
    public Result Close(string reason)
    {
        Guard.AgainstNullOrEmpty(reason);

        if (Status == AccountStatus.Closed)
            return Result.Failure("Account is already closed");

        if (!Balance.IsZero)
            return Result.Failure("Cannot close account with non-zero balance");

        Status = AccountStatus.Closed;
        ClosedAt = DateTimeOffset.UtcNow;

        AddDomainEvent(new AccountClosedEvent(Id, AccountNumber, CustomerId, reason));
        return Result.Success();
    }

    /// <summary>Applies a completed transaction to this account's balance.</summary>
    public Result ApplyCompletedTransaction(string transactionType, decimal amount, Currency currency, bool isDestination)
    {
        var validation = ValidateCompletedTransaction(transactionType, amount, currency, isDestination, out var isCredit);
        if (validation.IsFailure)
            return validation;

        var value = new Money(amount, currency);
        Balance = isCredit ? Balance.Add(value) : Balance.Subtract(value);
        return Result.Success();
    }

    /// <summary>Validates and applies a transfer to both accounts without partial mutation.</summary>
    public static Result ApplyTransfer(Account source, Account destination, decimal amount, Currency currency)
    {
        if (source is null || destination is null)
            return Result.Failure("Source and destination accounts are required", errorCode: InvalidTransferErrorCode);

        if (source.Id == Guid.Empty || destination.Id == Guid.Empty || source.Id == destination.Id)
            return Result.Failure("A transfer requires distinct valid accounts", errorCode: InvalidTransferErrorCode);

        var sourceValidation = source.ValidateCompletedTransaction("Transfer", amount, currency, false, out _);
        if (sourceValidation.IsFailure)
            return sourceValidation;

        var destinationValidation = destination.ValidateCompletedTransaction("Transfer", amount, currency, true, out _);
        if (destinationValidation.IsFailure)
            return destinationValidation;

        var value = new Money(amount, currency);
        var sourceBalance = source.Balance.Subtract(value);
        var destinationBalance = destination.Balance.Add(value);

        source.Balance = sourceBalance;
        destination.Balance = destinationBalance;

        return Result.Success();
    }

    private Result ValidateCompletedTransaction(
        string transactionType,
        decimal amount,
        Currency currency,
        bool isDestination,
        out bool isCredit)
    {
        isCredit = false;

        if (amount <= 0)
            return Result.Failure("Transaction amount must be greater than zero", errorCode: InvalidAmountErrorCode);

        if (currency is null)
            return Result.Failure("Transaction currency is required", errorCode: InvalidCurrencyErrorCode);

        switch (transactionType)
        {
            case "Deposit":
                isCredit = true;
                break;
            case "Withdrawal":
                isCredit = false;
                break;
            case "Transfer":
                isCredit = isDestination;
                break;
            default:
                return Result.Failure("Transaction type is not supported", errorCode: UnsupportedTransactionTypeErrorCode);
        }

        if (Status != AccountStatus.Active)
            return Result.Failure("Transactions can only be applied to an active account", errorCode: AccountInactiveErrorCode);

        if (Balance.Currency != currency)
            return Result.Failure("Transaction currency does not match account currency", errorCode: CurrencyMismatchErrorCode);

        var value = new Money(amount, currency);
        if (!isCredit && !Balance.IsGreaterThanOrEqual(value))
            return Result.Failure("Insufficient funds", errorCode: InsufficientFundsErrorCode);

        return Result.Success();
    }
}
