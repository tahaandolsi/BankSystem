using BankSystem.Account.Application.DTOs;
using BankSystem.Account.Application.Interfaces;
using BankSystem.Account.Domain.Entities;
using BankSystem.Account.Domain.Enums;
using BankSystem.Account.Infrastructure.Data;
using BankSystem.Shared.Domain.Common;
using BankSystem.Shared.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using AccountEntity = BankSystem.Account.Domain.Entities.Account;

namespace BankSystem.Account.Infrastructure.Services;

public sealed class AccountTransferAuthorizationService : IAccountTransferAuthorizationService
{
    private const string RequestIdIndexName = "UX_AccountTransferAuthorizations_CustomerId_RequestId";
    private const string ActiveOrConsumedBusinessRequestIndexName =
        "UX_AccountTransferAuthorizations_ActiveOrConsumedBusinessRequest";
    private readonly AccountDbContext _db;
    private readonly TimeSpan _authorizationLifetime;

    public AccountTransferAuthorizationService(AccountDbContext db, IConfiguration configuration)
    {
        _db = db;

        var lifetimeMinutes = configuration.GetValue<int?>(
            "Account:TransferAuthorizationLifetimeMinutes") ?? 1440;
        if (lifetimeMinutes is < 1 or > 43200)
        {
            throw new InvalidOperationException(
                "Account:TransferAuthorizationLifetimeMinutes must be between 1 and 43200.");
        }

        _authorizationLifetime = TimeSpan.FromMinutes(lifetimeMinutes);
    }

    public async Task<Result<AccountTransferAuthorizationDto>> CreateAsync(
        Guid requestedByUserId,
        Guid customerId,
        CreateAccountTransferAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        if (requestedByUserId == Guid.Empty
            || customerId == Guid.Empty
            || request is null
            || request.RequestId == Guid.Empty
            || request.BusinessRequestId == Guid.Empty
            || request.SourceAccountId == Guid.Empty
            || request.BeneficiaryAccountId == Guid.Empty
            || request.SourceAccountId == request.BeneficiaryAccountId
            || request.Amount <= 0
            || request.Amount > 9999999999999999.99m
            || decimal.Round(request.Amount, Currency.DecimalPlaces) != request.Amount
            || string.IsNullOrWhiteSpace(request.Currency))
        {
            return Failure("The transfer authorization request is invalid.", ErrorType.Validation);
        }

        var currencyCode = request.Currency.Trim().ToUpperInvariant();
        if (!Currency.IsValidCurrencyCode(currencyCode))
            return Failure("The currency is not supported.", ErrorType.Validation);

        var sourceAccount = await _db.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.Id == request.SourceAccountId, cancellationToken);
        if (sourceAccount is null || sourceAccount.CustomerId != customerId)
            return Failure("The source or beneficiary account is not available.", ErrorType.NotFound);

        var beneficiaryAccount = await _db.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.Id == request.BeneficiaryAccountId, cancellationToken);
        if (beneficiaryAccount is null)
            return Failure("The source or beneficiary account is not available.", ErrorType.NotFound);

        if (sourceAccount.Status != AccountStatus.Active
            || beneficiaryAccount.Status != AccountStatus.Active)
        {
            return Failure("Both accounts must be active.", ErrorType.Conflict);
        }

        if (sourceAccount.Balance.Currency.Code != currencyCode
            || beneficiaryAccount.Balance.Currency.Code != currencyCode)
        {
            return Failure("The requested currency must match both accounts.", ErrorType.Validation);
        }

        var existingRequest = await _db.AccountTransferAuthorizations
            .AsNoTracking()
            .SingleOrDefaultAsync(authorization =>
                authorization.CustomerId == customerId
                && authorization.RequestId == request.RequestId,
                cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (existingRequest is not null)
        {
            if (!MatchesRequest(existingRequest, requestedByUserId, customerId, request, currencyCode)
                || !existingRequest.IsActiveAt(now))
            {
                return Failure("The request identifier is already used or its authorization is no longer valid.",
                    ErrorType.Conflict);
            }

            return Result<AccountTransferAuthorizationDto>.Success(ToDto(existingRequest, now));
        }

        var previousBusinessRequests = await _db.AccountTransferAuthorizations
            .Where(authorization =>
                authorization.CustomerId == customerId
                && authorization.BusinessRequestId == request.BusinessRequestId)
            .ToListAsync(cancellationToken);

        if (previousBusinessRequests.Any(authorization =>
                !MatchesRequest(authorization, requestedByUserId, customerId, request, currencyCode)))
        {
            return Failure("The business request identifier is already linked to different transfer data.",
                ErrorType.Conflict);
        }

        if (previousBusinessRequests.Any(authorization =>
                authorization.Status == AccountTransferAuthorizationStatus.Consumed))
        {
            return Failure("The business request has already been used.", ErrorType.Conflict);
        }

        foreach (var previousAuthorization in previousBusinessRequests)
            previousAuthorization.ExpireIfNeeded(now);

        var activeAuthorization = previousBusinessRequests.FirstOrDefault(authorization =>
            authorization.IsActiveAt(now));
        if (activeAuthorization is not null)
            return Result<AccountTransferAuthorizationDto>.Success(ToDto(activeAuthorization, now));

        var authorization = AccountTransferAuthorization.Create(
            requestedByUserId,
            customerId,
            request.RequestId,
            request.BusinessRequestId,
            request.SourceAccountId,
            request.BeneficiaryAccountId,
            request.Amount,
            currencyCode,
            now,
            now.Add(_authorizationLifetime));

        _db.AccountTransferAuthorizations.Add(authorization);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsAuthorizationConflict(exception))
        {
            _db.Entry(authorization).State = EntityState.Detached;

            var concurrentAuthorization = await _db.AccountTransferAuthorizations
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate =>
                    candidate.CustomerId == customerId
                    && candidate.RequestId == request.RequestId,
                    cancellationToken);

            if (concurrentAuthorization is null)
            {
                concurrentAuthorization = await _db.AccountTransferAuthorizations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(candidate =>
                        candidate.CustomerId == customerId
                        && candidate.BusinessRequestId == request.BusinessRequestId
                        && (candidate.Status == AccountTransferAuthorizationStatus.Active
                            || candidate.Status == AccountTransferAuthorizationStatus.Consumed),
                        cancellationToken);
            }

            if (concurrentAuthorization is null)
                throw;

            if (concurrentAuthorization.Status == AccountTransferAuthorizationStatus.Consumed)
                return Failure("The business request has already been used.", ErrorType.Conflict);

            if (!MatchesRequest(concurrentAuthorization, requestedByUserId, customerId, request, currencyCode)
                || !concurrentAuthorization.IsActiveAt(DateTimeOffset.UtcNow))
            {
                return Failure("The request identifier is already used or its authorization is no longer valid.",
                    ErrorType.Conflict);
            }

            return Result<AccountTransferAuthorizationDto>.Success(
                ToDto(concurrentAuthorization, DateTimeOffset.UtcNow));
        }

        return Result<AccountTransferAuthorizationDto>.Success(ToDto(authorization, now));
    }

    public async Task<Result<AccountTransferAuthorization>> ValidateForExecutionAsync(
        Guid authorizationId,
        Guid businessRequestId,
        Guid transactionId,
        AccountEntity sourceAccount,
        AccountEntity beneficiaryAccount,
        decimal amount,
        Currency currency,
        CancellationToken cancellationToken)
    {
        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Transfer authorization must be checked inside the financial database transaction.");
        }

        if (authorizationId == Guid.Empty)
            return Result<AccountTransferAuthorization>.Failure(
                "An authorization identifier is required.", ErrorType.Validation);

        var authorization = await _db.AccountTransferAuthorizations
            .FromSqlInterpolated($"SELECT * FROM \"AccountTransferAuthorizations\" WHERE \"AuthorizationId\" = {authorizationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (authorization is null)
            return Result<AccountTransferAuthorization>.Failure(
                "The authorization was not found.", ErrorType.NotFound);

        await _db.Entry(authorization).ReloadAsync(cancellationToken);
        if (_db.Entry(authorization).State == EntityState.Detached)
            return Result<AccountTransferAuthorization>.Failure(
                "The authorization was not found.", ErrorType.NotFound);

        var businessRequestWasConsumed = await _db.AccountTransferAuthorizations
            .AsNoTracking()
            .AnyAsync(candidate =>
                candidate.CustomerId == authorization.CustomerId
                && candidate.BusinessRequestId == authorization.BusinessRequestId
                && candidate.Status == AccountTransferAuthorizationStatus.Consumed,
                cancellationToken);
        if (businessRequestWasConsumed)
            return Result<AccountTransferAuthorization>.Failure(
                "The business request has already been used.", ErrorType.Conflict);

        var validation = authorization.ValidateForExecution(
            businessRequestId,
            transactionId,
            sourceAccount,
            beneficiaryAccount,
            amount,
            currency,
            DateTimeOffset.UtcNow);

        if (validation.IsFailure)
        {
            return Result<AccountTransferAuthorization>.Failure(
                validation.Error,
                validation.ErrorType ?? ErrorType.Failure,
                validation.ErrorCode);
        }

        return Result<AccountTransferAuthorization>.Success(authorization);
    }

    private static bool MatchesRequest(
        AccountTransferAuthorization authorization,
        Guid requestedByUserId,
        Guid customerId,
        CreateAccountTransferAuthorizationRequest request,
        string currencyCode)
    {
        return authorization.MatchesRequest(
            requestedByUserId,
            customerId,
            request.BusinessRequestId,
            request.SourceAccountId,
            request.BeneficiaryAccountId,
            request.Amount,
            currencyCode);
    }

    private static AccountTransferAuthorizationDto ToDto(
        AccountTransferAuthorization authorization,
        DateTimeOffset now)
    {
        return new AccountTransferAuthorizationDto(
            authorization.AuthorizationId,
            authorization.BusinessRequestId,
            authorization.CreatedAt,
            authorization.ExpiresAt,
            authorization.GetStatusAt(now));
    }

    private static Result<AccountTransferAuthorizationDto> Failure(string message, ErrorType errorType)
    {
        return Result<AccountTransferAuthorizationDto>.Failure(message, errorType);
    }

    private static bool IsAuthorizationConflict(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException
                && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
                && postgresException.ConstraintName is RequestIdIndexName or ActiveOrConsumedBusinessRequestIndexName)
            {
                return true;
            }
        }

        return false;
    }
}
