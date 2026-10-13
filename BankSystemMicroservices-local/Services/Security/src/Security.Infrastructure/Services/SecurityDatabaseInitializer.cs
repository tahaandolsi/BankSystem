using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Security.Infrastructure.Services;

/// <summary>
/// Creates the local Security database when it does not exist yet.
/// This keeps local development independent from an installed SQL Server instance.
/// </summary>
public sealed class SecurityDatabaseInitializer(
    IConfiguration configuration,
    ILogger<SecurityDatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Security database connection string not configured");

        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new InvalidOperationException("Security database name is not configured");

        builder.InitialCatalog = "master";

        const int maxAttempts = 30;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(builder.ConnectionString);
                await connection.OpenAsync(cancellationToken);

                await using var command = connection.CreateCommand();
                command.CommandText = $"IF DB_ID(N'{databaseName.Replace("'", "''")}') IS NULL CREATE DATABASE [{databaseName.Replace("]", "]]" )}];";
                await command.ExecuteNonQueryAsync(cancellationToken);

                logger.LogInformation("Security database {DatabaseName} is ready.", databaseName);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex, "Waiting for SQL Server (attempt {Attempt}/{MaxAttempts})...", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new InvalidOperationException("SQL Server did not become available for the Security database.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
