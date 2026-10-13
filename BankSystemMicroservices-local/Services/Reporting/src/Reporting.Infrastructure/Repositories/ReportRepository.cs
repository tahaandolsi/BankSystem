using BankSystem.Reporting.Application.Interfaces;
using BankSystem.Reporting.Domain.Entities;
using BankSystem.Reporting.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BankSystem.Reporting.Infrastructure.Repositories;

public sealed class ReportRepository(ReportingDbContext db) : IReportRepository
{
    public Task AddAsync(ReportExecution execution, CancellationToken cancellationToken)
    {
        return db.Executions.AddAsync(execution, cancellationToken).AsTask();
    }

    public Task<ReportExecution?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return db.Executions
            .AsNoTracking()
            .FirstOrDefaultAsync(execution => execution.Id == id, cancellationToken);
    }

    public Task<ReportDefinition?> GetDefinitionAsync(Guid id, CancellationToken cancellationToken)
    {
        return db.Definitions
            .AsNoTracking()
            .FirstOrDefaultAsync(definition => definition.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<TransactionAnalytics>> GetAnalyticsByCustomerIdAsync(
        Guid customerId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var query = db.TransactionAnalytics
            .AsNoTracking()
            .Where(transaction => transaction.CustomerId == customerId);

        if (fromDate is not null)
            query = query.Where(transaction => transaction.CompletedAt >= fromDate);

        if (toDate is not null)
            query = query.Where(transaction => transaction.CompletedAt <= toDate);

        return await query
            .OrderByDescending(transaction => transaction.CompletedAt)
            .ToListAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        return db.SaveChangesAsync(cancellationToken);
    }
}
