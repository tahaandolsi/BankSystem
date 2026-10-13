using MediatR;

namespace BankSystem.Reporting.Application.Queries;

public record GetReportExecutionContentQuery(Guid Id, string RequestedBy) : IRequest<string?>;
