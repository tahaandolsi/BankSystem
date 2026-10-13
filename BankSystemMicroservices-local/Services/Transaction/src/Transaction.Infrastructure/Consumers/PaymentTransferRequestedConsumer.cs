using BankSystem.Shared.Domain.Events.Payment;
using BankSystem.Transaction.Infrastructure.Messaging;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace BankSystem.Transaction.Infrastructure.Consumers;

/// <summary>
/// Persists a Payment-linked Pending transaction without starting financial processing.
/// </summary>
public sealed class PaymentTransferRequestedConsumer(
    PaymentTransferCorrelationService correlationService,
    ILogger<PaymentTransferRequestedConsumer> logger)
    : IConsumer<PaymentTransferRequestedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<PaymentTransferRequestedIntegrationEvent> context)
    {
        var message = context.Message;
        if (message is null)
            throw new InvalidOperationException("Payment transfer command is required.");

        if (context.CorrelationId != message.PaymentId)
            throw new InvalidOperationException("Payment correlation identifier does not match the command.");

        var transactionId = await correlationService.CreateOrGetAsync(
            message,
            context.CancellationToken);

        logger.LogInformation(
            "Payment command {PaymentId} is durably associated with transaction {TransactionId}",
            message.PaymentId,
            transactionId);
    }
}
