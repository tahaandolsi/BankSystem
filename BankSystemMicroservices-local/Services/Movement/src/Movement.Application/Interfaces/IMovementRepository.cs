using MovementEntity = BankSystem.Movement.Domain.Entities.Movement;

namespace BankSystem.Movement.Application.Interfaces;

public interface IMovementRepository
{
    Task AddAsync(
        MovementEntity movement,
        CancellationToken cancellationToken);

    Task<MovementEntity?> GetAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<List<MovementEntity>> GetByAccountAsync(
        Guid accountId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken);

    Task SaveAsync(
        CancellationToken cancellationToken);
}