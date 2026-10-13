using BankSystem.Movement.Application.Interfaces;
using BankSystem.Movement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

using MovementEntity =
    BankSystem.Movement.Domain.Entities.Movement;

namespace BankSystem.Movement.Infrastructure.Repositories;

public sealed class MovementRepository(
    MovementDbContext db) : IMovementRepository
{
    public Task AddAsync(
        MovementEntity movement,
        CancellationToken cancellationToken)
    {
        return db.Movements
            .AddAsync(movement, cancellationToken)
            .AsTask();
    }

    public Task<MovementEntity?> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return db.Movements
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    public Task<List<MovementEntity>> GetByAccountAsync(
        Guid accountId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        var query = db.Movements
            .AsNoTracking()
            .Where(x => x.AccountId == accountId);

        if (from.HasValue)
        {
            query = query.Where(
                x => x.Timestamp >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(
                x => x.Timestamp <= to.Value);
        }

        return query
            .OrderByDescending(x => x.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public Task SaveAsync(
        CancellationToken cancellationToken)
    {
        return db.SaveChangesAsync(cancellationToken);
    }
}