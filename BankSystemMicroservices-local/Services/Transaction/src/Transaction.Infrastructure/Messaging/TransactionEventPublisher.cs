using BankSystem.Transaction.Application.Interfaces;
using BankSystem.Shared.Domain.Events.Transaction;
using MassTransit;
using Microsoft.Extensions.Logging;
namespace BankSystem.Transaction.Infrastructure.Messaging;
public sealed class TransactionEventPublisher(
    IPublishEndpoint publishEndpoint,
    ILogger<TransactionEventPublisher> logger) : ITransactionEventPublisher
{
    public async Task PublishAsync(TransactionExecutionRequestedIntegrationEvent @event, CancellationToken ct)
    {
        await publishEndpoint.Publish(@event, publishContext =>
        {
            publishContext.CorrelationId = @event.PaymentId ?? @event.TransactionId;
        }, ct);

        logger.LogInformation(
            "Transaction execution request queued for Account: {TransactionId}",
            @event.TransactionId);
    }

    public async Task PublishAsync(TransactionCompletedIntegrationEvent @event, CancellationToken ct)
    {
        await publishEndpoint.Publish(@event, publishContext =>
        {
            publishContext.CorrelationId = @event.PaymentId ?? @event.TransactionId;
        }, ct);

        logger.LogInformation(
            "Transaction workflow message queued for Account: {TransactionId}",
            @event.TransactionId);
    }
}
