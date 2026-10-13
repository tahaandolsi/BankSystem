using BankSystem.Movement.Application.Interfaces;
using BankSystem.Movement.Infrastructure.Consumers;
using BankSystem.Movement.Infrastructure.Data;
using BankSystem.Movement.Infrastructure.Repositories;
using BankSystem.Shared.Infrastructure.Extensions;
using BankSystem.Shared.Kernel.Common;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BankSystem.Movement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing");

        services.AddDbContext<MovementDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IMovementRepository, MovementRepository>();

        services.AddMassTransitOutboxSupport<MovementDbContext>(
            configuration,
            DatabaseEngine.PostgreSql,
            "Movement",
            (bus, context) => bus.ConfigureCrossServiceSubscription(
                context,
                "movement",
                "transaction",
                (endpoint, registrationContext) =>
                {
                    endpoint.ConfigureConsumer<TransactionCompletedConsumer>(registrationContext);
                    endpoint.ConfigureConsumer<TransactionFinanciallyCompletedConsumer>(registrationContext);
                }),
            consumers =>
            {
                consumers.AddConsumer<TransactionCompletedConsumer>();
                consumers.AddConsumer<TransactionFinanciallyCompletedConsumer>();
            });

        services.AddEnsureDatabaseCreated<MovementDbContext>();
        services.AddEnsurePostgreSqlOutboxSchema<MovementDbContext>();

        return services;
    }
}
