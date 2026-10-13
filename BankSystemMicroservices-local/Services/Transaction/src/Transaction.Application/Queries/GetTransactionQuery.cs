using BankSystem.Transaction.Application.DTOs;
using MediatR;
namespace BankSystem.Transaction.Application.Queries;
public record GetTransactionQuery(Guid Id) : IRequest<TransactionDto?>;
