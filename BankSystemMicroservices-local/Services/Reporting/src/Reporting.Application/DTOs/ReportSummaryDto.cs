namespace BankSystem.Reporting.Application.DTOs;

public record CurrencyReportSummaryDto(string Currency, decimal Deposits, decimal Withdrawals, decimal Transfers, decimal NetFlow);
public record ReportSummaryDto(int TransactionCount, IReadOnlyList<CurrencyReportSummaryDto> Totals);
