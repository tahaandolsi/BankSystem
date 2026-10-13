using BankSystem.Shared.Domain.Validation;
using MassTransit;
using MassTransit.RabbitMqTransport;

namespace BankSystem.Shared.Infrastructure.Extensions;

/// <summary>
/// Helpers for configuring cross-service MassTransit consumers on RabbitMQ.
/// </summary>
public static class CrossServiceConsumerExtensions
{
    private static readonly char[] InvalidCharacters = ['/', '\\', '#', '?'];

    public static void ConfigureCrossServiceSubscription(
        this IRabbitMqBusFactoryConfigurator cfg,
        IRegistrationContext context,
        string consumerServiceName,
        string sourceDomainName,
        Action<IRabbitMqReceiveEndpointConfigurator, IRegistrationContext> configureConsumers
    )
    {
        Guard.AgainstNull(context);
        Guard.AgainstNull(configureConsumers);
        Guard.AgainstNullOrEmpty(consumerServiceName);
        Guard.AgainstNullOrEmpty(sourceDomainName);
        Guard.Against(consumerServiceName.IndexOfAny(InvalidCharacters) >= 0,
            "consumerServiceName contains invalid characters.");
        Guard.Against(sourceDomainName.IndexOfAny(InvalidCharacters) >= 0,
            "sourceDomainName contains invalid characters.");

        var queueName = $"{consumerServiceName.ToLowerInvariant()}-{sourceDomainName.ToLowerInvariant()}";
        cfg.ReceiveEndpoint(queueName, endpoint =>
        {
            endpoint.PrefetchCount = 5;
            endpoint.ConcurrentMessageLimit = 3;
            // Publishers use one topic exchange per bounded context (for example
            // "transaction-events"). Bind the consumer queue to that exchange;
            // ConfigureConsumer then dispatches the received message by its type.
            endpoint.Bind($"{sourceDomainName.ToLowerInvariant()}-events");
            configureConsumers(endpoint, context);
        });
    }
}
