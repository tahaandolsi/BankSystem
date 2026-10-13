using BankSystem.Reporting.Application.DTOs;
using MediatR;
namespace BankSystem.Reporting.Application.Queries;
public record GetMyReportSummaryQuery(Guid CustomerId, DateTime? FromDate, DateTime? ToDate) : IRequest<ReportSummaryDto>;
