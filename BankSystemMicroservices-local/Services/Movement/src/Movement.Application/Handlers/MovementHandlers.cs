using BankSystem.Movement.Application.DTOs;
using BankSystem.Movement.Application.Interfaces;
using BankSystem.Movement.Application.Queries;
using MediatR;

namespace BankSystem.Movement.Application.Handlers;

public sealed class GetMovementsHandler(
    IMovementRepository repository)
    : IRequestHandler<GetMovementsQuery, IReadOnlyList<MovementDto>>
{
    public async Task<IReadOnlyList<MovementDto>> Handle(
        GetMovementsQuery request,
        CancellationToken cancellationToken)
    {
        var movements = await repository.GetByAccountAsync(
            request.AccountId,
            request.FromDate,
            request.ToDate,
            cancellationToken);

        return movements
            .Select(Map)
            .ToList();
    }

    private static MovementDto Map(
        BankSystem.Movement.Domain.Entities.Movement movement)
    {
        return new MovementDto(
            movement.Id,
            movement.TransactionId,
            movement.AccountId,
            movement.ToAccountId,
            movement.Amount,
            movement.Currency,
            movement.Type,
            movement.Description,
            movement.Reference,
            movement.Timestamp);
    }
}

public sealed class GetMovementHandler(
    IMovementRepository repository)
    : IRequestHandler<GetMovementQuery, MovementDto?>
{
    public async Task<MovementDto?> Handle(
        GetMovementQuery request,
        CancellationToken cancellationToken)
    {
        var movement = await repository.GetAsync(
            request.Id,
            cancellationToken);

        if (movement is null)
            return null;

        return new MovementDto(
            movement.Id,
            movement.TransactionId,
            movement.AccountId,
            movement.ToAccountId,
            movement.Amount,
            movement.Currency,
            movement.Type,
            movement.Description,
            movement.Reference,
            movement.Timestamp);
    }
}