using BankSystem.Notification.Application.Commands;
using BankSystem.Notification.Application.Queries;
using BankSystem.Shared.WebApiDefaults.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BankSystem.Notification.Api.Controllers;

[ApiController, Route("api/v1/notifications"), Authorize]
public class NotificationsController(IMediator mediator) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Mine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var clientId = User.FindFirst("clientId")?.Value;
        if (!Guid.TryParse(clientId, out var customerId) || customerId == Guid.Empty)
            return Unauthorized();

        // Temporary contract: Notification.UserId stores CustomerId, not the JWT sub.
        return Ok(await mediator.Send(new GetNotificationsByUserQuery(customerId.ToString(), page, pageSize), ct));
    }

    [HttpPost("send")]
    [Authorize(Policy = PolicyConstants.AdminAccess)]
    public async Task<IActionResult> Send(SendNotificationCommand command, CancellationToken ct)
        => Ok(await mediator.Send(command, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst("clientId")?.Value, out var customerId) || customerId == Guid.Empty)
            return NotFound();

        var notification = await mediator.Send(new GetNotificationQuery(id), ct);
        return notification is not null
            && Guid.TryParse(notification.UserId, out var ownerId) && ownerId == customerId
            ? Ok(notification) : NotFound();
    }
}
