using BankSystem.Account.Application.DTOs;
using BankSystem.Account.Domain.Entities;
using BankSystem.Shared.Domain.Common;
using CurrencyValue = BankSystem.Shared.Domain.ValueObjects.Currency;

namespace BankSystem.Account.Application.Interfaces;

public interface IAccountTransferAuthorizationService
{
    Task<Result<AccountTransferAuthorizationDto>> CreateAsync(
        Guid requestedByUserId,
        Guid customerId,
        CreateAccountTransferAuthorizationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Locks and validates an authorization inside the caller's database transaction.
    /// The caller must mark it consumed only after applying the transfer, then save the
    /// authorization, receipt, balances, and outbox message in that same transaction.
    /// </summary>
    Task<Result<AccountTransferAuthorization>> ValidateForExecutionAsync(
        Guid authorizationId,
        Guid businessRequestId,
        Guid transactionId,
        Account sourceAccount,
        Account beneficiaryAccount,
        decimal amount,
        CurrencyValue currency,
        CancellationToken cancellationToken);
}
