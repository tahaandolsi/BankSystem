using System.Text.Json;
using BankSystem.Reporting.Application.Commands;
using BankSystem.Reporting.Application.DTOs;
using BankSystem.Reporting.Application.Interfaces;
using BankSystem.Reporting.Application.Queries;
using BankSystem.Reporting.Domain.Entities;
using BankSystem.Reporting.Domain.Enums;
using MediatR;

namespace BankSystem.Reporting.Application.Handlers;

public sealed class GenerateReportHandler(IReportRepository repository)
    : IRequestHandler<GenerateReportCommand, ReportExecutionDto>
{
    public async Task<ReportExecutionDto> Handle(
        GenerateReportCommand request,
        CancellationToken cancellationToken)
    {
        if (request.CustomerId == Guid.Empty)
            throw new ArgumentException("A valid customer is required.");

        if (string.IsNullOrWhiteSpace(request.RequestedBy))
            throw new ArgumentException("An authenticated requester is required.");

        var definition = await repository.GetDefinitionAsync(
            request.ReportDefinitionId,
            cancellationToken);

        if (definition is null)
            throw new KeyNotFoundException("The report definition was not found.");

        if (!definition.IsActive)
            throw new ArgumentException("The report definition is inactive.");

        if (definition.Type != ReportType.TransactionSummary)
            throw new NotImplementedException("Only transaction summary reports are supported.");

        var execution = new ReportExecution
        {
            Id = Guid.NewGuid(),
            ReportDefinitionId = definition.Id,
            RequestedBy = request.RequestedBy,
            Status = ReportStatus.Running,
        };

        await repository.AddAsync(execution, cancellationToken);
        await repository.SaveAsync(cancellationToken);

        try
        {
            var transactions = await repository.GetAnalyticsByCustomerIdAsync(
                request.CustomerId,
                fromDate: null,
                toDate: null,
                cancellationToken);

            var summary = CreateSummary(transactions);
            execution.ContentJson = JsonSerializer.Serialize(
                summary,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true,
                });
            execution.Status = ReportStatus.Completed;
            execution.CompletedAt = DateTime.UtcNow;
            execution.ErrorMessage = null;

            await repository.SaveAsync(cancellationToken);
            return Map(execution);
        }
        catch
        {
            execution.Status = ReportStatus.Failed;
            execution.CompletedAt = DateTime.UtcNow;
            execution.ErrorMessage = "Transaction summary generation failed.";
            await repository.SaveAsync(CancellationToken.None);
            throw;
        }
    }

    private static ReportSummaryDto CreateSummary(IReadOnlyList<TransactionAnalytics> transactions)
    {
        var totals = transactions
            .GroupBy(transaction => transaction.Currency)
            .OrderBy(group => group.Key)
            .Select(group => new CurrencyReportSummaryDto(
                group.Key,
                group.Where(transaction => transaction.Type == "Deposit").Sum(transaction => transaction.Amount),
                group.Where(transaction => transaction.Type == "Withdrawal").Sum(transaction => transaction.Amount),
                group.Where(transaction => transaction.Type == "Transfer").Sum(transaction => transaction.Amount),
                group.Where(transaction => transaction.Type == "Deposit").Sum(transaction => transaction.Amount)
                    - group.Where(transaction => transaction.Type != "Deposit").Sum(transaction => transaction.Amount)))
            .ToList();

        return new ReportSummaryDto(transactions.Count, totals);
    }

    private static ReportExecutionDto Map(ReportExecution execution)
    {
        return new ReportExecutionDto(
            execution.Id,
            execution.ReportDefinitionId,
            execution.Status.ToString(),
            execution.RequestedAt,
            execution.CompletedAt,
            execution.FilePath,
            execution.ErrorMessage,
            execution.RequestedBy);
    }
}

public sealed class GetReportExecutionHandler(IReportRepository repository)
    : IRequestHandler<GetReportExecutionQuery, ReportExecutionDto?>
{
    public async Task<ReportExecutionDto?> Handle(
        GetReportExecutionQuery request,
        CancellationToken cancellationToken)
    {
        var execution = await repository.GetAsync(request.Id, cancellationToken);
        return execution is null ? null : Map(execution);
    }

    private static ReportExecutionDto Map(ReportExecution execution)
    {
        return new ReportExecutionDto(
            execution.Id,
            execution.ReportDefinitionId,
            execution.Status.ToString(),
            execution.RequestedAt,
            execution.CompletedAt,
            execution.FilePath,
            execution.ErrorMessage,
            execution.RequestedBy);
    }
}

public sealed class GetReportExecutionContentHandler(IReportRepository repository)
    : IRequestHandler<GetReportExecutionContentQuery, string?>
{
    public async Task<string?> Handle(
        GetReportExecutionContentQuery request,
        CancellationToken cancellationToken)
    {
        var execution = await repository.GetAsync(request.Id, cancellationToken);

        if (execution is null
            || !string.Equals(execution.RequestedBy, request.RequestedBy, StringComparison.Ordinal)
            || execution.Status != ReportStatus.Completed
            || string.IsNullOrWhiteSpace(execution.ContentJson))
        {
            return null;
        }

        return execution.ContentJson;
    }
}
