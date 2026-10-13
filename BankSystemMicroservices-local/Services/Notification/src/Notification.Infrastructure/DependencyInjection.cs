using BankSystem.Notification.Application.Interfaces;
using BankSystem.Notification.Infrastructure.Consumers;
using BankSystem.Notification.Infrastructure.Data;
using BankSystem.Notification.Infrastructure.Providers;
using BankSystem.Notification.Infrastructure.Repositories;
using BankSystem.Shared.Infrastructure.Extensions;
using BankSystem.Shared.Kernel.Common;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BankSystem.Notification.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing");

        services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationSender, LoggingNotificationSender>();

        services.AddMassTransitOutboxSupport<NotificationDbContext>(
            configuration,
            DatabaseEngine.PostgreSql,
            "Notification",
            (bus, context) => bus.ConfigureCrossServiceSubscription(
                context,
                "notification",
                "account",
                (endpoint, registrationContext) =>
                {
                    endpoint.UseEntityFrameworkOutbox<NotificationDbContext>(registrationContext);
                    endpoint.ConfigureConsumer<AccountTransactionAppliedConsumer>(registrationContext);
                }),
            consumers => consumers.AddConsumer<AccountTransactionAppliedConsumer>());

        services.AddEnsureDatabaseCreated<NotificationDbContext>();
        services.AddEnsurePostgreSqlOutboxSchema<NotificationDbContext>();

        return services;
    }
}
