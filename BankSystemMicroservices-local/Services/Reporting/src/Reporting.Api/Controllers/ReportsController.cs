using BankSystem.Reporting.Application.Commands;
using BankSystem.Reporting.Application.Queries;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BankSystem.Reporting.Api.Controllers;

[ApiController, Route("api/v1/reports"), Authorize]
public class ReportsController(IMediator mediator) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Mine([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, CancellationToken ct)
    {
        var clientId = User.FindFirst("clientId")?.Value;
        if (!Guid.TryParse(clientId, out var customerId) || customerId == Guid.Empty) return Unauthorized();
        return Ok(await mediator.Send(new GetMyReportSummaryQuery(customerId, fromDate, toDate), ct));
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate(GenerateReportCommand command, CancellationToken ct)
    {
        var subject = AuthenticatedSubject();
        var clientId = User.FindFirst("clientId")?.Value;
        if (string.IsNullOrWhiteSpace(subject)
            || !Guid.TryParse(clientId, out var customerId)
            || customerId == Guid.Empty)
        {
            return Unauthorized();
        }

        return Accepted(await mediator.Send(
            command with { CustomerId = customerId, RequestedBy = subject },
            ct));
    }

    [HttpGet("executions/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var subject = AuthenticatedSubject();
        if (string.IsNullOrWhiteSpace(subject)) return Unauthorized();
        var execution = await mediator.Send(new GetReportExecutionQuery(id), ct);
        return execution is not null && string.Equals(execution.RequestedBy, subject, StringComparison.Ordinal)
            ? Ok(execution) : NotFound();
    }

    [HttpGet("executions/{id:guid}/content")]
    public async Task<IActionResult> GetContent(Guid id, CancellationToken ct)
    {
        var subject = AuthenticatedSubject();
        if (string.IsNullOrWhiteSpace(subject)) return Unauthorized();

        var contentJson = await mediator.Send(
            new GetReportExecutionContentQuery(id, subject),
            ct);

        return contentJson is null
            ? NotFound()
            : Content(contentJson, "application/json");
    }

    private string? AuthenticatedSubject()
        => User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
