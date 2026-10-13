using BankSystem.Transaction.Domain.Enums;
using BankSystem.Transaction.Domain.ValueObjects;
using FluentAssertions;
using Xunit;
using System;

using TransactionEntity =
    BankSystem.Transaction.Domain.Entities.Transaction;

namespace BankSystem.Transaction.Domain.UnitTests;

public class TransactionTests
{
    [Fact]
    public void Create_ValidRequest_StartsPending()
    {
        var transaction = TransactionEntity.Create(
            Guid.NewGuid(),
            null,
            new Money(10, "eur"),
            TransactionType.Deposit,
            "test",
            "ref",
            "key");

        transaction.Status.Should().Be(TransactionStatus.Pending);
        transaction.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Complete_SetsCompleted()
    {
        var transaction = TransactionEntity.Create(
            Guid.NewGuid(),
            null,
            new Money(10, "USD"),
            TransactionType.Deposit,
            "test",
            "ref",
            "key");

        transaction.Complete();

        transaction.Status.Should().Be(TransactionStatus.Completed);
    }
}