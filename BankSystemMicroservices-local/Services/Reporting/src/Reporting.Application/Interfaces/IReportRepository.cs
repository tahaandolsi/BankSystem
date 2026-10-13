using BankSystem.Reporting.Domain.Entities;

namespace BankSystem.Reporting.Application.Interfaces;

public interface IReportRepository
{
    Task AddAsync(ReportExecution execution, CancellationToken cancellationToken);
    Task<ReportExecution?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<ReportDefinition?> GetDefinitionAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionAnalytics>> GetAnalyticsByCustomerIdAsync(
        Guid customerId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
}
