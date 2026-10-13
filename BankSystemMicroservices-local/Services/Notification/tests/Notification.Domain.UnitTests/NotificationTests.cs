using FluentAssertions;
using Xunit;
using System;

namespace BankSystem.Notification.Domain.UnitTests;public class NotificationTests{[Fact]public void Notification_DefaultsToPending(){var x=new BankSystem.Notification.Domain.Entities.Notification{Id=Guid.NewGuid(),UserId="u",Subject="s",Body="b"};x.Status.Should().Be(BankSystem.Notification.Domain.Enums.NotificationStatus.Pending);}}