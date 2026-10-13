using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BankSystem.Transaction.Application.Interfaces;
using BankSystem.Transaction.Infrastructure.Data;
using BankSystem.Transaction.Infrastructure.Consumers;
using BankSystem.Transaction.Infrastructure.Messaging;
using BankSystem.Transaction.Infrastructure.Repositories;
using BankSystem.Shared.Domain.Events.Account;
using BankSystem.Shared.Domain.Events.Payment;
using BankSystem.Shared.Domain.Events.Transaction;
using BankSystem.Shared.Infrastructure.Extensions;
using BankSystem.Shared.Kernel.Common;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Security.Authentication;
using Npgsql;

namespace BankSystem.Transaction.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing");

        services.AddDbContext<TransactionDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ITransactionEventPublisher, TransactionEventPublisher>();

        var paymentResultPublishingEnabled = configuration.GetValue<bool>(
            "PaymentRabbit:ResultPublishingEnabled");
        var paymentCommandConsumerEnabled = configuration.GetValue<bool>(
            "PaymentRabbit:CommandConsumerEnabled");
        var paymentCommandV2ConsumerEnabled = configuration.GetValue<bool>(
            "PaymentRabbit:CommandV2ConsumerEnabled");

        if ((paymentCommandConsumerEnabled || paymentCommandV2ConsumerEnabled)
            && !paymentResultPublishingEnabled)
        {
            throw new InvalidOperationException(
                "Payment command consumers require PaymentRabbit:ResultPublishingEnabled.");
        }

        services.AddMassTransitOutboxSupport<TransactionDbContext>(
            configuration,
            DatabaseEngine.PostgreSql,
            "Transaction",
            configureMessageTypes: (bus, registrationContext) =>
            {
                bus.ConfigureDomainPublishing(
                    "transaction",
                    typeof(TransactionExecutionRequestedIntegrationEvent),
                    typeof(TransactionCompletedIntegrationEvent),
                    typeof(TransactionFinanciallyCompletedIntegrationEvent));

                bus.ConfigureCrossServiceSubscription(
                    registrationContext,
                    "transaction",
                    "account",
                    (endpoint, consumerRegistrationContext) =>
                    {
                        endpoint.UseEntityFrameworkOutbox<TransactionDbContext>(consumerRegistrationContext);
                        endpoint.ConfigureConsumer<AccountTransactionAppliedConsumer>(consumerRegistrationContext);
                        endpoint.ConfigureConsumer<AccountTransactionRejectedConsumer>(consumerRegistrationContext);
                    });

            },
            configureAdditionalConsumers: consumers =>
            {
                consumers.AddConsumer<AccountTransactionAppliedConsumer>();
                consumers.AddConsumer<AccountTransactionRejectedConsumer>();
            });

        services.AddScoped(serviceProvider => new PaymentFinancialResultOutboxStore(
            serviceProvider.GetRequiredService<TransactionDbContext>(),
            paymentResultPublishingEnabled));
        services.AddScoped<PaymentTransferCorrelationService>();

        if (paymentResultPublishingEnabled || paymentCommandConsumerEnabled || paymentCommandV2ConsumerEnabled)
        {
            var paymentRabbitHost = configuration["PaymentRabbit:Host"]
                ?? throw new InvalidOperationException("PaymentRabbit:Host is required when Payment Rabbit features are enabled.");
            var paymentRabbitVirtualHost = configuration["PaymentRabbit:VirtualHost"] ?? "/bank-financial";
            if (!string.Equals(paymentRabbitVirtualHost, "/bank-financial", StringComparison.Ordinal))
                throw new InvalidOperationException("Payment Rabbit features must use the dedicated /bank-financial virtual host.");
            var paymentRabbitUsername = configuration["PaymentRabbit:Username"]
                ?? throw new InvalidOperationException("PaymentRabbit:Username is required when Payment Rabbit features are enabled.");
            var paymentRabbitPassword = configuration["PaymentRabbit:Password"]
                ?? throw new InvalidOperationException("PaymentRabbit:Password is required when Payment Rabbit features are enabled.");
            var paymentRabbitUseSsl = configuration.GetValue<bool>("PaymentRabbit:UseSsl");

            services.AddMassTransit<IPaymentFinancialBus>(bus =>
            {
                if (paymentCommandConsumerEnabled)
                    bus.AddConsumer<PaymentTransferRequestedConsumer>();
                if (paymentCommandV2ConsumerEnabled)
                    bus.AddConsumer<PaymentTransferRequestedV2Consumer>();

                bus.UsingRabbitMq((context, rabbit) =>
                {
                    rabbit.Host(new Uri(paymentRabbitHost), paymentRabbitVirtualHost, host =>
                    {
                        host.Username(paymentRabbitUsername);
                        host.Password(paymentRabbitPassword);
                        if (paymentRabbitUseSsl)
                            host.UseSsl(ssl => ssl.Protocol = SslProtocols.Tls12);
                    });

                    rabbit.UseRawJsonSerializer(RawSerializerOptions.AddTransportHeaders, isDefault: true);
                    rabbit.UseRawJsonDeserializer(RawSerializerOptions.AnyMessageType, isDefault: true);

                    if (paymentCommandConsumerEnabled)
                    {
                        rabbit.ReceiveEndpoint("transaction.create-from-payment.v1", endpoint =>
                        {
                            endpoint.ConfigureConsumeTopology = false;
                            endpoint.PrefetchCount = 5;
                            endpoint.ConcurrentMessageLimit = 3;
                            endpoint.Bind("banksystem.commands.v1", binding =>
                            {
                                binding.ExchangeType = "topic";
                                binding.RoutingKey = "bank.transaction.requested.v1";
                            });
                            endpoint.UseMessageRetry(retry =>
                            {
                                retry.Handle<DbUpdateException>();
                                retry.Handle<NpgsqlException>();
                                retry.Handle<TimeoutException>();
                                retry.Exponential(
                                    retryLimit: 3,
                                    minInterval: TimeSpan.FromSeconds(1),
                                    maxInterval: TimeSpan.FromSeconds(10),
                                    intervalDelta: TimeSpan.FromSeconds(2));
                            });
                            endpoint.ConfigureConsumer<PaymentTransferRequestedConsumer>(
                                context);
                        });
                    }

                    if (paymentCommandV2ConsumerEnabled)
                    {
                        rabbit.ReceiveEndpoint("transaction.create-from-payment.v2", endpoint =>
                        {
                            endpoint.ConfigureConsumeTopology = false;
                            endpoint.PrefetchCount = 5;
                            endpoint.ConcurrentMessageLimit = 3;
                            endpoint.Bind("banksystem.commands.v2", binding =>
                            {
                                binding.ExchangeType = "topic";
                                binding.RoutingKey = "bank.transaction.requested.v2";
                            });
                            endpoint.UseMessageRetry(retry =>
                            {
                                retry.Handle<DbUpdateException>();
                                retry.Handle<NpgsqlException>();
                                retry.Handle<TimeoutException>();
                                retry.Exponential(
                                    retryLimit: 3,
                                    minInterval: TimeSpan.FromSeconds(1),
                                    maxInterval: TimeSpan.FromSeconds(10),
                                    intervalDelta: TimeSpan.FromSeconds(2));
                            });
                            endpoint.ConfigureConsumer<PaymentTransferRequestedV2Consumer>(context);
                        });
                    }

                    ConfigurePaymentResultTopology(rabbit);
                });
            });

            services.AddHostedService<PaymentTransactionAcceptedOutboxRelay>();
            if (paymentResultPublishingEnabled)
                services.AddHostedService<PaymentFinancialResultOutboxRelay>();
        }

        services.AddEnsureDatabaseCreated<TransactionDbContext>();
        services.AddEnsurePostgreSqlOutboxSchema<TransactionDbContext>();

        return services;
    }

    private static void ConfigurePaymentResultTopology(IRabbitMqBusFactoryConfigurator rabbit)
    {
        rabbit.Message<PaymentTransactionAcceptedIntegrationEvent>(message =>
            message.SetEntityName("banksystem.payment.acceptance.v1"));
        rabbit.Publish<PaymentTransactionAcceptedIntegrationEvent>(message =>
            message.ExchangeType = "topic");
        rabbit.SendTopology.UseRoutingKeyFormatter<PaymentTransactionAcceptedIntegrationEvent>(
            _ => "bank.payment.accepted.v1");

        rabbit.Message<PaymentTransactionCompletedIntegrationEvent>(message =>
            message.SetEntityName("banksystem.payment.results.v1"));
        rabbit.Publish<PaymentTransactionCompletedIntegrationEvent>(message =>
            message.ExchangeType = "topic");
        rabbit.SendTopology.UseRoutingKeyFormatter<PaymentTransactionCompletedIntegrationEvent>(
            _ => "bank.payment.completed.v1");

        rabbit.Message<PaymentTransactionRejectedIntegrationEvent>(message =>
            message.SetEntityName("banksystem.payment.results.v1"));
        rabbit.Publish<PaymentTransactionRejectedIntegrationEvent>(message =>
            message.ExchangeType = "topic");
        rabbit.SendTopology.UseRoutingKeyFormatter<PaymentTransactionRejectedIntegrationEvent>(
            _ => "bank.payment.rejected.v1");
    }
}
