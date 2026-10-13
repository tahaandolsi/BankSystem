using BankSystem.Account.Domain.Enums;
using BankSystem.Shared.Domain.Events.Account;
using BankSystem.Shared.Domain.ValueObjects;
using FluentAssertions;
using AccountEntity = BankSystem.Account.Domain.Entities.Account;

namespace BankSystem.Account.Domain.UnitTests.Entities;

public class AccountTests
{
    private static AccountEntity CreateTestAccount(
        Guid customerId,
        AccountType accountType,
        Currency? currency = null
    )
    {
        currency ??= Currency.USD;
        return AccountEntity.CreateNew(customerId, accountType, currency);
    }

    [Fact]
    public void CreateNew_ValidParameters_ShouldCreateAccount()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        const AccountType accountType = AccountType.Checking;

        // Act
        var account = CreateTestAccount(customerId, accountType);

        // Assert
        account.Should().NotBeNull();
        account.CustomerId.Should().Be(customerId);
        account.Type.Should().Be(accountType);
        account.Balance.Amount.Should().Be(0);
        account.Status.Should().Be(AccountStatus.PendingActivation);
        account.DomainEvents.Should().ContainSingle(e => e is AccountCreatedEvent);
    }

    [Fact]
    public void Activate_ValidAccount_ShouldActivateAccount()
    {
        // Arrange
        var account = CreateTestAccount(Guid.NewGuid(), AccountType.Checking);

        // Act
        account.Activate();

        // Assert
        account.Status.Should().Be(AccountStatus.Active);
        account.DomainEvents.Should().ContainSingle(e => e is AccountActivatedEvent);
    }

    [Fact]
    public void Suspend_ValidAccount_ShouldSuspendAccount()
    {
        // Arrange
        var account = CreateTestAccount(Guid.NewGuid(), AccountType.Checking);

        // Act
        account.Suspend("Suspicious activity");

        // Assert
        account.Status.Should().Be(AccountStatus.Suspended);
        account.DomainEvents.Should().ContainSingle(e => e is AccountSuspendedEvent);
    }

    [Fact]
    public void Freeze_ValidAccount_ShouldFreezeAccount()
    {
        // Arrange
        var account = CreateTestAccount(Guid.NewGuid(), AccountType.Checking);

        // Act
        account.Freeze("Suspicious activity");

        // Assert
        account.Status.Should().Be(AccountStatus.Frozen);
        account.DomainEvents.Should().ContainSingle(e => e is AccountFrozenEvent);
    }

    [Fact]
    public void Close_ValidAccount_ShouldCloseAccount()
    {
        // Arrange
        var currency = Currency.EUR;
        var account = CreateTestAccount(Guid.NewGuid(), AccountType.Checking, currency);
        account.Activate();

        // Act
        account.Close("Customer request");

        // Assert
        account.Status.Should().Be(AccountStatus.Closed);
        account.DomainEvents.Should().ContainSingle(e => e is AccountClosedEvent);
    }

    [Fact]
    public void ApplyCompletedTransaction_ActiveAccountWithSufficientFunds_ShouldApplyWithdrawal()
    {
        // Arrange
        var account = CreateTestAccount(Guid.NewGuid(), AccountType.Checking);
        account.Activate();
        account.ApplyCompletedTransaction("Deposit", 100m, Currency.USD, false);

        // Act
        var result = account.ApplyCompletedTransaction("Withdrawal", 40m, Currency.USD, false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        account.Balance.Amount.Should().Be(60m);
    }

    [Fact]
    public void ApplyCompletedTransaction_InactiveAccount_ShouldBeRejected()
    {
        // Arrange
        var account = CreateTestAccount(Guid.NewGuid(), AccountType.Checking);

        // Act
        var result = account.ApplyCompletedTransaction("Withdrawal", 10m, Currency.USD, false);

        // Assert
        result.IsFailure.Should().BeTrue();
        account.Balance.Amount.Should().Be(0m);
    }

    [Fact]
    public void ApplyCompletedTransaction_MismatchedCurrency_ShouldBeRejected()
    {
        // Arrange
        var account = CreateTestAccount(Guid.NewGuid(), AccountType.Checking);
        account.Activate();

        // Act
        var result = account.ApplyCompletedTransaction("Withdrawal", 10m, Currency.EUR, false);

        // Assert
        result.IsFailure.Should().BeTrue();
        account.Balance.Amount.Should().Be(0m);
    }

    [Fact]
    public void ApplyCompletedTransaction_InsufficientFunds_ShouldBeRejected()
    {
        // Arrange
        var account = CreateTestAccount(Guid.NewGuid(), AccountType.Checking);
        account.Activate();

        // Act
        var result = account.ApplyCompletedTransaction("Withdrawal", 1m, Currency.USD, false);

        // Assert
        result.IsFailure.Should().BeTrue();
        account.Balance.Amount.Should().Be(0m);
    }
}
