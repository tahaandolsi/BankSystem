using System.Text.Json;
using BankSystem.Shared.Domain.Events.Payment;
using BankSystem.Transaction.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BankSystem.Transaction.Infrastructure.Messaging;

public sealed class PaymentFinancialResultOutboxRelay(
    IServiceScopeFactory scopeFactory,
    IPaymentFinancialBus paymentBus,
    ILogger<PaymentFinancialResultOutboxRelay> logger)
    : BackgroundService
{
    private const int BatchSize = 10;
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<PaymentFinancialResultOutboxMessage> messages;
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
                logger.LogError(
                    "Payment result outbox could not be read ({ErrorType})",
                    exception.GetType().Name);
                await Task.Delay(PollDelay, stoppingToken);
                continue;
            }

            foreach (var message in messages)
            {
                try
                {
                    await Publish(message, stoppingToken);
                    await MarkPublished(message, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        "Payment result outbox message {MessageId} will be retried ({ErrorType})",
                        message.Id,
                        exception.GetType().Name);
                    try
                    {
                        await MarkFailed(message, exception.GetType().Name, stoppingToken);
                    }
                    catch (Exception persistenceException)
                    {
                        logger.LogError(
                            "Payment result outbox failure could not be recorded for {MessageId} ({ErrorType})",
                            message.Id,
                            persistenceException.GetType().Name);
                    }
                }
            }

            await Task.Delay(PollDelay, stoppingToken);
        }
    }

    private async Task<IReadOnlyList<PaymentFinancialResultOutboxMessage>> ClaimBatch(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<PaymentFinancialResultOutboxStore>();
        return await store.ClaimBatch(cancellationToken);
    }

    private async Task Publish(
        PaymentFinancialResultOutboxMessage message,
        CancellationToken cancellationToken)
    {
        if (message.EventType == "Completed")
        {
            if (message.RoutingKey != "bank.payment.completed.v1")
                throw new InvalidOperationException("Payment completion outbox routing key is invalid.");

            var result = JsonSerializer.Deserialize<PaymentTransactionCompletedIntegrationEvent>(
                message.PayloadJson,
                JsonOptions) ?? throw new InvalidOperationException("Payment completion result is invalid.");

            await paymentBus.Publish(result, context =>
            {
                context.MessageId = message.Id;
                context.CorrelationId = message.PaymentId;
            }, cancellationToken);
            return;
        }

        if (message.EventType == "Rejected")
        {
            if (message.RoutingKey != "bank.payment.rejected.v1")
                throw new InvalidOperationException("Payment rejection outbox routing key is invalid.");

            var result = JsonSerializer.Deserialize<PaymentTransactionRejectedIntegrationEvent>(
                message.PayloadJson,
                JsonOptions) ?? throw new InvalidOperationException("Payment rejection result is invalid.");

            await paymentBus.Publish(result, context =>
            {
                context.MessageId = message.Id;
                context.CorrelationId = message.PaymentId;
            }, cancellationToken);
            return;
        }

        throw new InvalidOperationException("Payment result outbox event type is unknown.");
    }

    private async Task MarkPublished(
        PaymentFinancialResultOutboxMessage message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<PaymentFinancialResultOutboxStore>();
        await store.MarkPublished(message.Id, message.LockUntilUtc, cancellationToken);
    }

    private async Task MarkFailed(
        PaymentFinancialResultOutboxMessage message,
        string errorType,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<PaymentFinancialResultOutboxStore>();
        await store.RecordFailure(message.Id, message.LockUntilUtc, errorType, cancellationToken);
    }
}
