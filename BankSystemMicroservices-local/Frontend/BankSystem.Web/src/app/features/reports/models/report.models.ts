export interface CurrencyReportSummary { currency: string; deposits: number; withdrawals: number; transfers: number; netFlow: number; }
export interface ReportSummary { transactionCount: number; totals: CurrencyReportSummary[]; }
