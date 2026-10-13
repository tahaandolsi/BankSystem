using BankSystem.Transaction.Application.Commands;
using BankSystem.Transaction.Application.DTOs;
using BankSystem.Transaction.Application.Interfaces;
using BankSystem.Transaction.Application.Queries;
using BankSystem.Shared.Domain.Events.Transaction;
using BankSystem.Transaction.Domain.ValueObjects;
using MediatR;

using TransactionEntity = BankSystem.Transaction.Domain.Entities.Transaction;

namespace BankSystem.Transaction.Application.Handlers;

public sealed class CreateTransactionHandler(
    ITransactionRepository repo,
    ITransactionEventPublisher bus)
    : IRequestHandler<CreateTransactionCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(
        CreateTransactionCommand request,
        CancellationToken cancellationToken)
    {
        if (await repo.ExistsByIdempotencyKeyAsync(
                request.IdempotencyKey,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Duplicate idempotency key.");
        }

        var transaction = TransactionEntity.Create(
            request.AccountId,
            request.ToAccountId,
            new Money(request.Amount, request.Currency),
            request.Type,
            request.Description,
            request.Reference,
            request.IdempotencyKey);

        await repo.AddAsync(transaction, cancellationToken);

        await bus.PublishAsync(
            new TransactionExecutionRequestedIntegrationEvent(
                TransactionExecutionRequestedIntegrationEvent.CurrentContractVersion,
                transaction.Id,
                transaction.AccountId,
                transaction.ToAccountId,
                transaction.Amount,
                transaction.Currency,
                transaction.Type.ToString(),
                transaction.Description,
                transaction.Reference,
                DateTimeOffset.UtcNow,
                transaction.PaymentId,
                transaction.AuthorizationId),
            cancellationToken);

        await repo.SaveChangesAsync(cancellationToken);

        return Map(transaction);
    }

    private static TransactionDto Map(TransactionEntity transaction)
    {
        return new TransactionDto(
            transaction.Id,
            transaction.AccountId,
            transaction.ToAccountId,
            transaction.Amount,
            transaction.Currency,
            transaction.Type.ToString(),
            transaction.Status.ToString(),
            transaction.Description,
            transaction.Reference,
            transaction.ProcessedAt == default ? null : transaction.ProcessedAt);
    }
}

public sealed class GetTransactionHandler(
    ITransactionRepository repo)
    : IRequestHandler<GetTransactionQuery, TransactionDto?>
{
    public async Task<TransactionDto?> Handle(
        GetTransactionQuery request,
        CancellationToken cancellationToken)
    {
        var transaction = await repo.GetAsync(
            request.Id,
            cancellationToken);

        if (transaction is null)
            return null;

        return new TransactionDto(
            transaction.Id,
            transaction.AccountId,
            transaction?.ToAccountId,
            transaction.Amount,
            transaction?.Currency,
            transaction?.Type.ToString(),
            transaction?.Status.ToString(),
            transaction?.Description,
            transaction?.Reference,
            transaction.ProcessedAt == default ? null : transaction.ProcessedAt);
    }
}
