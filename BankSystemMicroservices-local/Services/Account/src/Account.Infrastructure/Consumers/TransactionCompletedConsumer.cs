using BankSystem.Account.Infrastructure.Data;
using BankSystem.Account.Application.Interfaces;
using BankSystem.Shared.Domain.Events.Account;
using BankSystem.Shared.Domain.Events.Transaction;
using BankSystem.Shared.Domain.ValueObjects;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;

namespace BankSystem.Account.Infrastructure.Consumers;

public sealed class TransactionCompletedConsumer(
    AccountDbContext db,
    IAccountTransferAuthorizationService authorizationService,
    IConfiguration configuration) :
    IConsumer<TransactionCompletedIntegrationEvent>,
    IConsumer<TransactionExecutionRequestedIntegrationEvent>
{
    private readonly bool _authorizedTransferExecutionEnabled = configuration.GetValue<bool>(
        "Account:AuthorizedTransferExecutionEnabled");

    public Task Consume(ConsumeContext<TransactionCompletedIntegrationEvent> context)
    {
        var message = context.Message;
        if (message.WorkflowVersion != TransactionCompletedIntegrationEvent.CurrentWorkflowVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported transaction workflow version: {message.WorkflowVersion}.");
        }

        return ProcessAsync(context, new ExecutionRequest(
            message.TransactionId,
            message.AccountId,
            message.ToAccountId,
            message.Amount,
            message.Currency,
            message.Type,
            message.PaymentId,
            message.AuthorizationId));
    }

    public Task Consume(ConsumeContext<TransactionExecutionRequestedIntegrationEvent> context)
    {
        var message = context.Message;
        if (message.ContractVersion != TransactionExecutionRequestedIntegrationEvent.CurrentContractVersion
            || message.RequestedAt == default
            || message.RequestedAt.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException("Unsupported or invalid transaction execution request.");
        }

        return ProcessAsync(context, new ExecutionRequest(
            message.TransactionId,
            message.AccountId,
            message.ToAccountId,
            message.Amount,
            message.Currency,
            message.Type,
            message.PaymentId,
            message.AuthorizationId));
    }

    private async Task ProcessAsync(ConsumeContext context, ExecutionRequest message)
    {
        // Authorized transfer execution stays disabled unless an operator explicitly enables it.
        // Do this before creating a receipt or locking financial rows.
        if (message.Type == "Transfer"
            && message.AuthorizationId is Guid authorizationId
            && authorizationId != Guid.Empty
            && !_authorizedTransferExecutionEnabled)
        {
            throw new InvalidOperationException("Authorized transfer execution is disabled.");
        }

        // A retried consumer can reuse this scoped DbContext. Detach only the tracked
        // Account aggregates touched by this message, including their owned values, so
        // the locked queries below materialize those aggregates from the current rows.
        Guid[] accountIds = message.Type == "Transfer" && message.ToAccountId is Guid destinationId
            && destinationId != message.AccountId
                ? new[] { message.AccountId, destinationId }.Distinct().OrderBy(id => id).ToArray()
                : new[] { message.AccountId };
        var accountIdSet = accountIds.ToHashSet();
        var trackedEntries = db.ChangeTracker.Entries().ToArray();
        var entriesToDetach = trackedEntries
            .Where(entry => IsAccountAggregateEntry(entry)
                && accountIdSet.Contains(GetOwningAccountId(entry, trackedEntries)))
            .OrderByDescending(entry => GetOwnershipDepth(entry.Metadata))
            .ToArray();

        // Materialize the detach list before changing the ChangeTracker. Owned leaves
        // are detached before their Account root; unrelated entities stay tracked.
        foreach (var entry in entriesToDetach)
            entry.State = EntityState.Detached;

        // Claim the business transaction before touching balances. The unique key makes
        // concurrent deliveries of the same TransactionId serialize at the database.
        // This statement participates in the MassTransit EF Consumer Outbox transaction.
        var claimedAt = DateTimeOffset.UtcNow;
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "AccountTransactionReceipts"
                ("TransactionId", "Outcome", "RejectionCode", "ProcessedAt", "AppliedAt")
            VALUES
                ({message.TransactionId}, 'Applied', NULL, {claimedAt}, {claimedAt})
            ON CONFLICT ("TransactionId") DO NOTHING
            """,
            context.CancellationToken);

        if (claimed == 0)
            return;

        var initialRejectionCode = GetInitialRejectionCode(message);
        if (initialRejectionCode is not null)
        {
            await RecordRejectionAsync(initialRejectionCode);
            return;
        }

        // Lock all affected accounts in a stable order. Consumer Outbox owns the
        // surrounding PostgreSQL transaction; SELECT ... FOR UPDATE holds these locks
        // through the balance, receipt, and outgoing-message commit.
        var lockedAccounts = new Dictionary<Guid, BankSystem.Account.Domain.Entities.Account>();
        foreach (var accountId in accountIds)
        {
            var account = await db.Accounts
                .FromSqlInterpolated($"SELECT * FROM \"Accounts\" WHERE \"Id\" = {accountId} FOR UPDATE")
                .SingleOrDefaultAsync(context.CancellationToken);
            if (account is null)
            {
                await RecordRejectionAsync(AccountTransactionRejectedIntegrationEvent.AccountNotFound);
                return;
            }

            lockedAccounts.Add(accountId, account);
        }

        var source = lockedAccounts[message.AccountId];
        var currency = new Currency(message.Currency);

        if (message.Type == "Transfer")
        {
            var destination = lockedAccounts[message.ToAccountId!.Value];
            var authorizationResult = await authorizationService.ValidateForExecutionAsync(
                message.AuthorizationId!.Value,
                message.PaymentId!.Value,
                message.TransactionId,
                source,
                destination,
                message.Amount,
                currency,
                context.CancellationToken);
            if (authorizationResult.IsFailure)
            {
                await RecordRejectionAsync(
                    AccountTransactionRejectedIntegrationEvent.TransferAuthorizationInvalid);
                return;
            }

            var transferResult = BankSystem.Account.Domain.Entities.Account.ApplyTransfer(
                source,
                destination,
                message.Amount,
                currency);
            if (transferResult.IsFailure)
            {
                var rejectionCode = GetRejectionCode(transferResult.ErrorCode);
                await RecordRejectionAsync(rejectionCode);
                return;
            }

            var consumed = authorizationResult.Value!.MarkConsumed(
                message.TransactionId,
                DateTimeOffset.UtcNow);
            if (consumed.IsFailure)
            {
                throw new InvalidOperationException(
                    "The transfer authorization could not be consumed after the transfer was applied.");
            }
        }
        else
        {
            var sourceResult = source.ApplyCompletedTransaction(message.Type, message.Amount, currency, false);
            if (sourceResult.IsFailure)
            {
                var rejectionCode = GetRejectionCode(sourceResult.ErrorCode);
                await RecordRejectionAsync(rejectionCode);
                return;
            }
        }

        var processedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(context.CancellationToken);
        await UpdateReceiptAsync(AccountTransactionReceiptOutcome.Applied, null, processedAt);
        await context.Publish(new AccountTransactionAppliedIntegrationEvent(message.TransactionId, source.Id, source.CustomerId,
            message.Amount, message.Currency, message.Type, source.Balance.Amount, processedAt, message.ToAccountId), context.CancellationToken);

        async Task RecordRejectionAsync(string rejectionCode)
        {
            var rejectedAt = DateTimeOffset.UtcNow;
            await UpdateReceiptAsync(AccountTransactionReceiptOutcome.Rejected, rejectionCode, rejectedAt);
            await context.Publish(
                new AccountTransactionRejectedIntegrationEvent(
                    message.TransactionId,
                    message.AccountId,
                    message.ToAccountId,
                    message.Amount,
                    message.Currency,
                    message.Type,
                    rejectionCode,
                    rejectedAt),
                context.CancellationToken);
        }

        async Task UpdateReceiptAsync(
            AccountTransactionReceiptOutcome outcome,
            string? rejectionCode,
            DateTimeOffset processedAt)
        {
            var updated = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "AccountTransactionReceipts"
                SET "Outcome" = {outcome.ToString()},
                    "RejectionCode" = {rejectionCode},
                    "ProcessedAt" = {processedAt}
                WHERE "TransactionId" = {message.TransactionId}
                """,
                context.CancellationToken);

            if (updated != 1)
                throw new InvalidOperationException("The Account transaction receipt could not be finalized.");
        }
    }

    private static string? GetInitialRejectionCode(ExecutionRequest message)
    {
        if (message.Amount <= 0
            || decimal.Round(message.Amount, Currency.DecimalPlaces) != message.Amount)
        {
            return AccountTransactionRejectedIntegrationEvent.InvalidAmount;
        }

        if (!Currency.IsValidCurrencyCode(message.Currency))
            return AccountTransactionRejectedIntegrationEvent.InvalidCurrency;

        if (message.Type is not ("Deposit" or "Withdrawal" or "Transfer"))
            return AccountTransactionRejectedIntegrationEvent.UnsupportedTransactionType;

        if (message.Type == "Transfer"
            && (message.AccountId == Guid.Empty
                || message.ToAccountId is null
                || message.ToAccountId == Guid.Empty
                || message.ToAccountId == message.AccountId))
        {
            return AccountTransactionRejectedIntegrationEvent.InvalidTransfer;
        }

        if (message.Type == "Transfer"
            && (message.PaymentId is null
                || message.PaymentId == Guid.Empty
                || message.AuthorizationId is null
                || message.AuthorizationId == Guid.Empty))
        {
            return AccountTransactionRejectedIntegrationEvent.TransferAuthorizationRequired;
        }

        if (message.Type != "Transfer" && message.AuthorizationId is not null)
            return AccountTransactionRejectedIntegrationEvent.InvalidTransfer;

        return null;
    }

    private static string GetRejectionCode(string? errorCode) => errorCode switch
    {
        AccountTransactionRejectedIntegrationEvent.InvalidAmount => AccountTransactionRejectedIntegrationEvent.InvalidAmount,
        AccountTransactionRejectedIntegrationEvent.InvalidCurrency => AccountTransactionRejectedIntegrationEvent.InvalidCurrency,
        AccountTransactionRejectedIntegrationEvent.UnsupportedTransactionType => AccountTransactionRejectedIntegrationEvent.UnsupportedTransactionType,
        AccountTransactionRejectedIntegrationEvent.InvalidTransfer => AccountTransactionRejectedIntegrationEvent.InvalidTransfer,
        AccountTransactionRejectedIntegrationEvent.AccountInactive => AccountTransactionRejectedIntegrationEvent.AccountInactive,
        AccountTransactionRejectedIntegrationEvent.CurrencyMismatch => AccountTransactionRejectedIntegrationEvent.CurrencyMismatch,
        AccountTransactionRejectedIntegrationEvent.InsufficientFunds => AccountTransactionRejectedIntegrationEvent.InsufficientFunds,
        AccountTransactionRejectedIntegrationEvent.AccountNotFound => AccountTransactionRejectedIntegrationEvent.AccountNotFound,
        _ => throw new InvalidOperationException("Account returned an unrecognized transaction validation code.")
    };

    private static bool IsAccountAggregateEntry(EntityEntry entry)
    {
        var entityType = entry.Metadata;
        while (entityType.IsOwned())
        {
            var ownership = entityType.FindOwnership();
            if (ownership is null)
                return false;

            entityType = ownership.PrincipalEntityType;
        }

        return entityType.ClrType == typeof(BankSystem.Account.Domain.Entities.Account);
    }

    private static Guid GetOwningAccountId(EntityEntry entry, IReadOnlyList<EntityEntry> trackedEntries)
    {
        var current = entry;
        while (current.Metadata.IsOwned())
        {
            var ownership = current.Metadata.FindOwnership()
                ?? throw new InvalidOperationException("A tracked Account-owned entity has no ownership metadata.");

            var owner = trackedEntries.FirstOrDefault(candidate =>
                candidate.Metadata == ownership.PrincipalEntityType
                && ForeignKeyMatches(current, candidate, ownership));

            current = owner
                ?? throw new InvalidOperationException("A tracked Account-owned entity has no tracked owner.");
        }

        if (current.Entity is not BankSystem.Account.Domain.Entities.Account account)
            throw new InvalidOperationException("A tracked Account aggregate did not resolve to an Account entity.");

        return account.Id;
    }

    private static bool ForeignKeyMatches(EntityEntry dependent, EntityEntry principal, IForeignKey ownership)
    {
        if (ownership.Properties.Count != ownership.PrincipalKey.Properties.Count)
            return false;

        for (var index = 0; index < ownership.Properties.Count; index++)
        {
            var foreignKeyValue = dependent.Property(ownership.Properties[index].Name).CurrentValue;
            var principalKeyValue = principal.Property(ownership.PrincipalKey.Properties[index].Name).CurrentValue;
            if (!Equals(foreignKeyValue, principalKeyValue))
                return false;
        }

        return true;
    }

    private static int GetOwnershipDepth(IEntityType entityType)
    {
        var depth = 0;
        while (entityType.IsOwned())
        {
            var ownership = entityType.FindOwnership();
            if (ownership is null)
                break;

            depth++;
            entityType = ownership.PrincipalEntityType;
        }

        return depth;
    }

    private sealed record ExecutionRequest(
        Guid TransactionId,
        Guid AccountId,
        Guid? ToAccountId,
        decimal Amount,
        string Currency,
        string Type,
        Guid? PaymentId,
        Guid? AuthorizationId);
}
