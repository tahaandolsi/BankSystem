using BankSystem.Reporting.Application.DTOs;
using BankSystem.Reporting.Application.Interfaces;
using BankSystem.Reporting.Application.Queries;
using MediatR;
namespace BankSystem.Reporting.Application.Handlers;
public sealed class GetMyReportSummaryHandler(IReportRepository repository) : IRequestHandler<GetMyReportSummaryQuery, ReportSummaryDto>
{ public async Task<ReportSummaryDto> Handle(GetMyReportSummaryQuery request,CancellationToken ct){var analytics=await repository.GetAnalyticsByCustomerIdAsync(request.CustomerId,request.FromDate,request.ToDate,ct);var totals=analytics.GroupBy(x=>x.Currency).Select(group=>new CurrencyReportSummaryDto(group.Key,group.Where(x=>x.Type=="Deposit").Sum(x=>x.Amount),group.Where(x=>x.Type=="Withdrawal").Sum(x=>x.Amount),group.Where(x=>x.Type=="Transfer").Sum(x=>x.Amount),group.Where(x=>x.Type=="Deposit").Sum(x=>x.Amount)-group.Where(x=>x.Type!="Deposit").Sum(x=>x.Amount))).ToList();return new ReportSummaryDto(analytics.Count,totals);}}
