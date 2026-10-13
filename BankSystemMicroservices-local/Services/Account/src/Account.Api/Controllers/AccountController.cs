using System.Net.Mime;
using System.Security.Claims;
using BankSystem.Shared.WebApiDefaults.Constants;
using BankSystem.Account.Application.Commands;
using BankSystem.Account.Application.DTOs;
using BankSystem.Account.Application.Interfaces;
using BankSystem.Account.Application.Queries;
using BankSystem.Shared.Domain.Validation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BankSystem.Account.Api.Controllers;

/// <summary>
/// Controller for managing bank accounts and account-related operations.
/// </summary>
[Route("api/v1/accounts")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
[Authorize]
public class AccountController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly IAccountTransferAuthorizationService _transferAuthorizationService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IMediator mediator,
        IAccountTransferAuthorizationService transferAuthorizationService,
        ILogger<AccountController> logger)
    {
        Guard.AgainstNull(mediator);
        Guard.AgainstNull(transferAuthorizationService);
        Guard.AgainstNull(logger);

        _mediator = mediator;
        _transferAuthorizationService = transferAuthorizationService;
        _logger = logger;
    }

    /// <summary>Creates or returns an authorization for one exact transfer request.</summary>
    [HttpPost("transfer-authorizations")]
    [ProducesResponseType(typeof(AccountTransferAuthorizationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountTransferAuthorizationDto>> CreateTransferAuthorization(
        [FromBody] CreateAccountTransferAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedIdentity(out var userId, out var customerId))
            return Unauthorized();

        var result = await _transferAuthorizationService.CreateAsync(
            userId,
            customerId,
            request,
            cancellationToken);

        if (result.IsFailure)
            return HandleFailure(result, "Transfer Authorization");

        return Ok(result.Value);
    }

    /// <summary>
    /// Creates a new customer account with the specified details.
    /// </summary>
    /// <param name="command">The account creation request containing customer and account details</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Returns the created account details including the generated account number.</returns>
    /// <response code="201">Account created successfully</response>
    /// <response code="400">Invalid request data or business rule violation</response>
    /// <response code="409">Account already exists for customer</response>
    /// <response code="500">Internal server error</response>
    [HttpPost]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AccountDto>> CreateAccount(
        [FromBody] CreateAccountCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var userName = User.Identity?.Name;

        _logger.LogInformation(
            "Creating account for user {UserName} with type {AccountType}",
            userName,
            command.AccountType
        );

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Failed to create account for user {UserName}: {Error}",
                userName,
                result.Error
            );
            return HandleFailure(result, command.ValidationErrorTitle());
        }

        _logger.LogInformation(
            "Account {AccountId} created successfully for user {UserName}",
            result.Value!.Id,
            userName
        );

        return StatusCode(201, result.Value);
    }

    /// <summary>
    /// Retrieves account information by account number.
    /// </summary>
    /// <param name="accountId">The account ID to retrieve</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Account details if found</returns>
    /// <response code="200">Account found and returned</response>
    /// <response code="404">Account not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("{accountId:guid}")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AccountDto>> GetAccountById(
        [FromRoute] Guid accountId,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation("Retrieving account with number {AccountId}", accountId);

        var query = new GetAccountByIdQuery(accountId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsSuccess)
        {
            if (result.Value is null || !CanAccessAccount(result.Value.CustomerId))
                return NotFound();

            return Ok(result.Value);
        }

        _logger.LogWarning("Account {AccountId} not found", accountId);
        return HandleFailure(result, "Account Not Found");
    }

    /// <summary>
    /// Retrieves all accounts for the authenticated user.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>List of accounts for the customer</returns>
    /// <response code="200">Accounts found and returned</response>
    /// <response code="404">Customer not found or no accounts exist</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("customer/me")]
    [ProducesResponseType(typeof(IEnumerable<AccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IEnumerable<AccountDto>>> GetAccountsByCustomerId(
        CancellationToken cancellationToken = default
    )
    {
        var userName = User.Identity?.Name;
        _logger.LogInformation("Retrieving accounts for user {UserName}", userName);

        if (!Guid.TryParse(User.FindFirst("clientId")?.Value, out var customerId) || customerId == Guid.Empty)
            return Unauthorized();

        var query = new GetAccountsByCustomerIdQuery();
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        _logger.LogWarning("No accounts found for user {UserName}", userName);
        return HandleFailure(result, "Accounts Not Found");
    }

    /// <summary>
    /// Activates an account.
    /// </summary>
    /// <param name="command">The account activation request</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>No content if successful</returns>
    /// <response code="204">Account activated successfully</response>
    /// <response code="400">Invalid request data or business rule violation</response>
    /// <response code="404">Account not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPut("activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> ActivateAccount(
        [FromBody] ActivateAccountCommand command,
        CancellationToken cancellationToken = default
    )
    {
        return await HandleAccountAction(
            command,
            "Activating",
            "activate",
            "activated",
            cancellationToken
        );
    }

    /// <summary>
    /// Freezes an account.
    /// </summary>
    /// <param name="command">The account freezing request</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>No content if successful</returns>
    /// <response code="204">Account frozen successfully</response>
    /// <response code="400">Invalid request data or business rule violation</response>
    /// <response code="404">Account not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPut("freeze")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> FreezeAccount(
        [FromBody] FreezeAccountCommand command,
        CancellationToken cancellationToken = default
    )
    {
        return await HandleAccountAction(
            command,
            "Freezing",
            "freeze",
            "frozen",
            cancellationToken
        );
    }

    /// <summary>
    /// Suspends an account.
    /// </summary>
    /// <param name="command">The account suspension request with reason</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>No content if successful</returns>
    /// <response code="204">Account suspended successfully</response>
    /// <response code="400">Invalid request data or business rule violation</response>
    /// <response code="404">Account not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPut("suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> SuspendAccount(
        [FromBody] SuspendAccountCommand command,
        CancellationToken cancellationToken = default
    )
    {
        return await HandleAccountAction(
            command,
            "Suspending",
            "suspend",
            "suspended",
            cancellationToken
        );
    }

    /// <summary>
    /// Closes an account.
    /// </summary>
    /// <param name="command">The account closure request with reason</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>No content if successful</returns>
    /// <response code="204">Account closed successfully</response>
    /// <response code="400">Invalid request data or business rule violation</response>
    /// <response code="404">Account not found</response>
    /// <response code="500">Internal server error</response>
    [HttpDelete("close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> CloseAccount(
        [FromBody] CloseAccountCommand command,
        CancellationToken cancellationToken = default
    )
    {
        return await HandleAccountAction(command, "Closing", "close", "closed", cancellationToken);
    }

    private async Task<ActionResult> HandleAccountAction<TCommand>(
        TCommand command,
        string actionPresentTense,
        string actionVerb,
        string actionPastTense,
        CancellationToken cancellationToken
    )
        where TCommand : IAccountActionCommand
    {
        var account = await _mediator.Send(new GetAccountByIdQuery(command.AccountId), cancellationToken);
        if (account.IsFailure)
            return HandleFailure(account, "Account Not Found");
        if (account.Value is null || !CanAccessAccount(account.Value.CustomerId))
            return NotFound();

        _logger.LogInformation(
            "{Action} account {AccountId}",
            actionPresentTense,
            command.AccountId
        );

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Failed to {ActionVerb} account {AccountId}: {Error}",
                actionVerb,
                command.AccountId,
                result.Error
            );

            return HandleFailure(result, command.ValidationErrorTitle());
        }

        _logger.LogInformation(
            "Account {AccountId} {ActionPastTense} successfully",
            command.AccountId,
            actionPastTense
        );
        return NoContent();
    }

    private bool CanAccessAccount(Guid customerId)
        => User.IsInRole(RoleConstants.Admin) || User.IsInRole(RoleConstants.Manager)
            || (Guid.TryParse(User.FindFirst("clientId")?.Value, out var clientId)
                && clientId != Guid.Empty && clientId == customerId);

    private bool TryGetAuthenticatedIdentity(out Guid userId, out Guid customerId)
    {
        userId = Guid.Empty;
        customerId = Guid.Empty;

        if (User.Identity?.IsAuthenticated != true)
            return false;

        // Security signs both subject and clientId from the same user record.
        // ClaimTypes.NameIdentifier is the validated JWT subject after claim mapping.
        var subjectValues = User.FindAll(ClaimTypes.NameIdentifier)
            .Concat(User.FindAll("sub"))
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var clientValues = User.FindAll("clientId")
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return subjectValues.Length == 1
            && clientValues.Length == 1
            && Guid.TryParse(subjectValues[0], out userId)
            && userId != Guid.Empty
            && Guid.TryParse(clientValues[0], out customerId)
            && customerId != Guid.Empty;
    }
}
