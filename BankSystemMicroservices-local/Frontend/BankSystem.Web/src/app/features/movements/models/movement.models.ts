export interface Movement {
  id: string;
  transactionId: string;
  accountId: string;
  toAccountId: string | null;
  amount: number;
  currency: string;
  type: string;
  description: string;
  reference: string;
  timestamp: string;
}

export interface MovementFilters {
  fromDate?: string;
  toDate?: string;
  page?: number;
  pageSize?: number;
}
