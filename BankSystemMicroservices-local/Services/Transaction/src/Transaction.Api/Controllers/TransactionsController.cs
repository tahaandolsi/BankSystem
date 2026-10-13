using BankSystem.Shared.WebApiDefaults.Services;
using BankSystem.Transaction.Application.Commands;
using BankSystem.Transaction.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BankSystem.Transaction.Api.Controllers;

[ApiController, Route("api/v1/transactions")]
[Authorize]
public class TransactionsController(
    IMediator mediator,
    AccountOwnershipVerifier ownership
) : ControllerBase
{
    [HttpPost("deposit")]
    public Task<IActionResult> Deposit(CreateTransactionCommand request, CancellationToken ct) =>
        Create(request with { Type = BankSystem.Transaction.Domain.Enums.TransactionType.Deposit }, ct);

    [HttpPost("withdrawal")]
    public Task<IActionResult> Withdrawal(CreateTransactionCommand request, CancellationToken ct) =>
        Create(request with { Type = BankSystem.Transaction.Domain.Enums.TransactionType.Withdrawal }, ct);

    [HttpPost("transfer")]
    public Task<IActionResult> Transfer(CreateTransactionCommand request, CancellationToken ct) =>
        Create(request with { Type = BankSystem.Transaction.Domain.Enums.TransactionType.Transfer }, ct);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var transaction = await mediator.Send(new GetTransactionQuery(id), ct);
        if (transaction is null)
            return NotFound();

        var ownershipResult = await ownership.VerifyAsync(HttpContext, transaction.AccountId, ct);
        return ownershipResult switch
        {
            AccountOwnershipResult.Owner => Ok(transaction),
            AccountOwnershipResult.Unauthenticated => Unauthorized(),
            AccountOwnershipResult.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable),
            _ => NotFound(),
        };
    }

    private async Task<IActionResult> Create(CreateTransactionCommand request, CancellationToken ct)
    {
        if (request.Type == BankSystem.Transaction.Domain.Enums.TransactionType.Transfer)
        {
            return Conflict(new
            {
                error = "HTTP transfers are unavailable until durable Account authorization is connected."
            });
        }

        var ownershipResult = await ownership.VerifyAsync(HttpContext, request.AccountId, ct);
        switch (ownershipResult)
        {
            case AccountOwnershipResult.Owner:
                break;
            case AccountOwnershipResult.Unauthenticated:
                return Unauthorized();
            case AccountOwnershipResult.Unavailable:
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            default:
                return Forbid();
        }

        try
        {
            return Accepted(await mediator.Send(request, ct));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }
}
