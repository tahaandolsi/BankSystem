namespace BankSystem.Account.Application.DTOs;

public sealed record AccountTransferAuthorizationDto(
    Guid AuthorizationId,
    Guid BusinessRequestId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Status);
