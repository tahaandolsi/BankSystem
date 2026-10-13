using FluentAssertions;
using System;
using Xunit;

namespace BankSystem.Reporting.Domain.UnitTests;public class ReportingTests{[Fact]public void Execution_DefaultsToPending(){var x=new BankSystem.Reporting.Domain.Entities.ReportExecution{Id=Guid.NewGuid(),ReportDefinitionId=Guid.NewGuid(),RequestedBy="u"};x.Status.Should().Be(BankSystem.Reporting.Domain.Enums.ReportStatus.Pending);}}