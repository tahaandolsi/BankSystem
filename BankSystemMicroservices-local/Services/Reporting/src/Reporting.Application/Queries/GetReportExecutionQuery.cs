using BankSystem.Reporting.Application.DTOs;using MediatR;namespace BankSystem.Reporting.Application.Queries;public record GetReportExecutionQuery(Guid Id):IRequest<ReportExecutionDto?>;
