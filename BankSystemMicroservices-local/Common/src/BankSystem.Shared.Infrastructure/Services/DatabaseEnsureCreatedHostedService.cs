using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BankSystem.Shared.Infrastructure.Services;

/// <summary>
/// Creates the EF Core schema at startup for local services that do not yet
/// have generated migrations. This is intended for the local Docker stack.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class DatabaseEnsureCreatedHostedService<TContext> : IHostedService
    where TContext : DbContext
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseEnsureCreatedHostedService<TContext>> _logger;

    public DatabaseEnsureCreatedHostedService(
        IServiceProvider serviceProvider,
        ILogger<DatabaseEnsureCreatedHostedService<TContext>> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 30;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                await context.Database.EnsureCreatedAsync(cancellationToken);
                _logger.LogInformation("Database schema for {ContextType} is ready.", typeof(TContext).Name);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Waiting for database for {ContextType} (attempt {Attempt}/{MaxAttempts})...",
                    typeof(TContext).Name, attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Database did not become available for {typeof(TContext).Name}.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
