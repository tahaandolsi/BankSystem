using BankSystem.Movement.Application.Queries;
using BankSystem.Shared.WebApiDefaults.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BankSystem.Movement.Api.Controllers;

[ApiController, Route("api/v1/movements"), Authorize]
public class MovementsController(IMediator mediator, AccountOwnershipVerifier ownership) : ControllerBase
{
    [HttpGet("account/{accountId:guid}")]
    public async Task<IActionResult> ByAccount(Guid accountId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var ownershipResult = await ownership.VerifyAsync(HttpContext, accountId, ct);
        if (ownershipResult == AccountOwnershipResult.Unauthenticated) return Unauthorized();
        if (ownershipResult == AccountOwnershipResult.Unavailable) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (ownershipResult != AccountOwnershipResult.Owner) return NotFound();
        return Ok(await mediator.Send(new GetMovementsQuery(accountId, fromDate, toDate, page, pageSize), ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var movement = await mediator.Send(new GetMovementQuery(id), ct);
        if (movement is null) return NotFound();
        var ownershipResult = await ownership.VerifyAsync(HttpContext, movement.AccountId, ct);
        return ownershipResult switch
        {
            AccountOwnershipResult.Owner => Ok(movement),
            AccountOwnershipResult.Unauthenticated => Unauthorized(),
            AccountOwnershipResult.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable),
            _ => NotFound(),
        };
    }
}
