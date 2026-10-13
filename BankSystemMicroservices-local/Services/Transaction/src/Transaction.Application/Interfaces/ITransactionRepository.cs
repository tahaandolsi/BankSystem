using TransactionEntity = BankSystem.Transaction.Domain.Entities.Transaction;

namespace BankSystem.Transaction.Application.Interfaces;

public interface ITransactionRepository
{
    Task AddAsync(
        TransactionEntity entity,
        CancellationToken cancellationToken);

    Task<TransactionEntity?> GetAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<TransactionEntity?> GetByPaymentIdAsync(
        Guid paymentId,
        CancellationToken cancellationToken);

    Task<bool> ExistsByIdempotencyKeyAsync(
        string key,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(
        CancellationToken cancellationToken);
}
