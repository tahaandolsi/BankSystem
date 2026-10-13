using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BankSystem.Reporting.Infrastructure.Data;

public sealed class ReportingSchemaUpgradeHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE IF EXISTS \"TransactionAnalytics\" ADD COLUMN IF NOT EXISTS \"CustomerId\" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DO $$ BEGIN IF to_regclass('\"TransactionAnalytics\"') IS NOT NULL THEN CREATE INDEX IF NOT EXISTS \"IX_TransactionAnalytics_CustomerId\" ON \"TransactionAnalytics\" (\"CustomerId\"); END IF; END $$;", cancellationToken);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
