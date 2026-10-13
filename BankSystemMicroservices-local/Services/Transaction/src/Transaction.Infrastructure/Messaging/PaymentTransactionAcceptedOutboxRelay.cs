using System.Text.Json;
using BankSystem.Shared.Domain.Events.Payment;
using BankSystem.Transaction.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BankSystem.Transaction.Infrastructure.Messaging;

public sealed class PaymentTransactionAcceptedOutboxRelay(
    IServiceScopeFactory scopeFactory,
    IPaymentFinancialBus paymentBus,
    ILogger<PaymentTransactionAcceptedOutboxRelay> logger) : BackgroundService
{
    private const int BatchSize = 10;
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<PaymentTransactionAcceptedOutboxMessage> messages;
            try
            {
                messages = await ClaimBatch(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError("Payment acceptance outbox could not be read ({ErrorType})", exception.GetType().Name);
                await Task.Delay(PollDelay, stoppingToken);
                continue;
            }

            foreach (var message in messages)
            {
                try
                {
                    var accepted = JsonSerializer.Deserialize<PaymentTransactionAcceptedIntegrationEvent>(
                        message.PayloadJson, JsonOptions)
                        ?? throw new InvalidOperationException("Payment acceptance outbox payload is invalid.");

                    await paymentBus.Publish(accepted, context =>
                    {
                        context.MessageId = message.Id;
                        context.CorrelationId = message.PaymentId;
                    }, stoppingToken);

                    await MarkPublished(message, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogWarning("Payment acceptance {MessageId} will be retried ({ErrorType})",
                        message.Id, exception.GetType().Name);
                    try
                    {
                        await RecordFailure(message, exception.GetType().Name, stoppingToken);
                    }
                    catch (Exception persistenceException)
                    {
                        logger.LogError("Payment acceptance retry could not be recorded for {MessageId} ({ErrorType})",
                            message.Id, persistenceException.GetType().Name);
                    }
                }
            }

            await Task.Delay(PollDelay, stoppingToken);
        }
    }

    private async Task<IReadOnlyList<PaymentTransactionAcceptedOutboxMessage>> ClaimBatch(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var now = TruncateToMicroseconds(DateTime.UtcNow);
        var lockUntil = now.Add(LeaseDuration);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var messages = await db.PaymentTransactionAcceptedOutboxMessages
            .FromSqlInterpolated($"""
                SELECT * FROM "PaymentTransactionAcceptedOutboxMessages"
                WHERE ("Status" = 'Pending' AND "NextAttemptAtUtc" <= {now})
                   OR ("Status" = 'Processing' AND "LockUntilUtc" <= {now})
                ORDER BY "CreatedAtUtc"
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
            message.Claim(lockUntil);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages;
    }

    private async Task MarkPublished(
        PaymentTransactionAcceptedOutboxMessage message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var storedMessage = await db.PaymentTransactionAcceptedOutboxMessages.SingleOrDefaultAsync(
            candidate => candidate.Id == message.Id
                && candidate.Status == "Processing"
                && candidate.LockUntilUtc == message.LockUntilUtc,
            cancellationToken);
        if (storedMessage is null)
            return;

        storedMessage.MarkPublished(TruncateToMicroseconds(DateTime.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordFailure(
        PaymentTransactionAcceptedOutboxMessage message,
        string errorType,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var storedMessage = await db.PaymentTransactionAcceptedOutboxMessages.SingleOrDefaultAsync(
            candidate => candidate.Id == message.Id
                && candidate.Status == "Processing"
                && candidate.LockUntilUtc == message.LockUntilUtc,
            cancellationToken);
        if (storedMessage is null)
            return;

        storedMessage.RecordFailure(errorType, TruncateToMicroseconds(DateTime.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static DateTime TruncateToMicroseconds(DateTime value)
    {
        return new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
    }
}
