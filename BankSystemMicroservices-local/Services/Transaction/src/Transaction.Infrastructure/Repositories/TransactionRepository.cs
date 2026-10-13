using BankSystem.Transaction.Application.Interfaces;
using BankSystem.Transaction.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

using TransactionEntity =
    BankSystem.Transaction.Domain.Entities.Transaction;

namespace BankSystem.Transaction.Infrastructure.Repositories;

public sealed class TransactionRepository(
    TransactionDbContext db) : ITransactionRepository
{
    public Task AddAsync(
        TransactionEntity entity,
        CancellationToken cancellationToken)
    {
        return db.Transactions
            .AddAsync(entity, cancellationToken)
            .AsTask();
    }

    public Task<TransactionEntity?> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return db.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    public Task<TransactionEntity?> GetByPaymentIdAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        return db.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                transaction => transaction.PaymentId == paymentId,
                cancellationToken);
    }

    public Task<bool> ExistsByIdempotencyKeyAsync(
        string key,
        CancellationToken cancellationToken)
    {
        return db.Transactions
            .AnyAsync(
                x => x.IdempotencyKey == key,
                cancellationToken);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        return db.SaveChangesAsync(cancellationToken);
    }
}
