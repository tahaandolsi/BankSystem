using FluentAssertions;
using Xunit;
using System;

namespace BankSystem.Movement.Domain.UnitTests;public class MovementTests{[Fact]public void Movement_CanBeCreated(){var x=new BankSystem.Movement.Domain.Entities.Movement{Id=Guid.NewGuid(),AccountId=Guid.NewGuid(),Amount=10,Type="Deposit"};x.Amount.Should().Be(10);x.Type.Should().Be("Deposit");}}