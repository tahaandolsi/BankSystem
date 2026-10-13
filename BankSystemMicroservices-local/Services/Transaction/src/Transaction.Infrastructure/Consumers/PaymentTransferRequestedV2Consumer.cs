using BankSystem.Shared.Domain.Events.Payment;
using BankSystem.Transaction.Infrastructure.Messaging;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace BankSystem.Transaction.Infrastructure.Consumers;

/// <summary>
/// Correlates an authorized Payment transfer and requests Account execution.
/// </summary>
public sealed class PaymentTransferRequestedV2Consumer(
    PaymentTransferCorrelationService correlationService,
    ILogger<PaymentTransferRequestedV2Consumer> logger)
    : IConsumer<PaymentTransferRequestedV2IntegrationEvent>
{
    public async Task Consume(ConsumeContext<PaymentTransferRequestedV2IntegrationEvent> context)
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
            "Authorized Payment command {PaymentId} is durably associated with transaction {TransactionId}",
            message.PaymentId,
            transactionId);
    }
}
