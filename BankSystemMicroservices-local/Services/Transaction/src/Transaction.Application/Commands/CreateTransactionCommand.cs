using BankSystem.Transaction.Application.DTOs;
using BankSystem.Transaction.Domain.Enums;
using MediatR;
namespace BankSystem.Transaction.Application.Commands;
public record CreateTransactionCommand(Guid AccountId, Guid? ToAccountId, decimal Amount, string Currency, TransactionType Type, string Description, string Reference, string IdempotencyKey) : IRequest<TransactionDto>;
