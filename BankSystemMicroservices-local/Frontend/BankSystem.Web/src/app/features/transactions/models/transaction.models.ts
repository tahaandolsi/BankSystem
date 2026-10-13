export interface TransactionRequest {
  accountId: string;
  toAccountId: string | null;
  amount: number;
  currency: string;
  description: string;
  reference: string;
  idempotencyKey: string;
}

export interface Transaction {
  id: string;
  accountId: string;
  toAccountId: string | null;
  amount: number;
  currency: string;
  type: string;
  status: string;
  description: string;
  reference: string;
  processedAt: string;
}

export interface PaymentTransferRequest {
  operationType: 'BANK_TRANSFER';
  sourceAccountId: string;
  beneficiaryAccountId: string;
  amount: number;
  currency: string;
  merchantReference?: string;
}

export interface PaymentStatusResponse {
  paymentId: string;
  sourceAccountId: string;
  beneficiaryAccountId: string;
  amount: number;
  currency: string;
  status: string;
  fraudAnalysisState: string | null;
  transactionId: string | null;
  failureReasonCode: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface PaymentHistoryResponse {
  items: PaymentStatusResponse[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
