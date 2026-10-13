using BankSystem.Shared.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BankSystem.Shared.Infrastructure.Extensions;

/// <summary>
/// Shared database migration extensions for all microservices
/// </summary>
public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Configures automatic database migration to be applied when the application starts
    /// </summary>
    /// <typeparam name="TContext">The DbContext type</typeparam>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddAutomaticMigrations<TContext>(
        this IServiceCollection services
    )
        where TContext : DbContext
    {
        // Validate that TContext is registered
        if (services.All(sd => sd.ServiceType != typeof(TContext)))
        {
            throw new InvalidOperationException(
                $"DbContext {typeof(TContext).Name} must be registered before calling AddAutomaticMigrations"
            );
        }

        services.AddHostedService<DatabaseMigrationHostedService<TContext>>();
        return services;
    }
    /// <summary>
    /// Creates the EF Core database schema when migrations are not available yet.
    /// Intended for local Docker development.
    /// </summary>
    public static IServiceCollection AddEnsureDatabaseCreated<TContext>(
        this IServiceCollection services
    ) where TContext : DbContext
    {
        if (services.All(sd => sd.ServiceType != typeof(TContext)))
        {
            throw new InvalidOperationException(
                $"DbContext {typeof(TContext).Name} must be registered before calling AddEnsureDatabaseCreated");
        }

        services.AddHostedService<DatabaseEnsureCreatedHostedService<TContext>>();
        return services;
    }

    /// <summary>
    /// Ensures the MassTransit Entity Framework outbox tables exist in an
    /// existing PostgreSQL database. The operation is idempotent.
    /// </summary>
    public static IServiceCollection AddEnsurePostgreSqlOutboxSchema<TContext>(
        this IServiceCollection services
    ) where TContext : DbContext
    {
        if (services.All(sd => sd.ServiceType != typeof(TContext)))
        {
            throw new InvalidOperationException(
                $"DbContext {typeof(TContext).Name} must be registered before calling AddEnsurePostgreSqlOutboxSchema");
        }

        services.AddHostedService<PostgreSqlOutboxSchemaHostedService<TContext>>();
        return services;
    }

}
