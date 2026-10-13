export type AccountType = 'Checking' | 'Savings' | 'Business';

export interface Account {
  id: string;
  accountNumber: string;
  customerId: string;
  accountType: AccountType;
  balance: number;
  currency: string;
  status: string;
  createdAt: string;
  updatedAt: string | null;
}

export interface CreateAccountRequest {
  accountType: AccountType;
  currency: string;
}
