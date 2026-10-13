import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../../core/api/api.config';
import {
  PaymentStatusResponse,
  PaymentHistoryResponse,
  PaymentTransferRequest,
  Transaction,
  TransactionRequest,
} from '../models/transaction.models';

export type TransactionOperation = 'deposit' | 'withdrawal' | 'transfer';

@Injectable({ providedIn: 'root' })
export class TransactionService {
  private readonly http = inject(HttpClient);
  private readonly endpoint = `${API_BASE_URL}/transactions`;
  private readonly paymentsEndpoint = `${API_BASE_URL}/payments`;

  create(operation: TransactionOperation, request: Omit<TransactionRequest, 'idempotencyKey'>): Observable<Transaction> {
    return this.http.post<Transaction>(`${this.endpoint}/${operation}`, {
      ...request,
      idempotencyKey: crypto.randomUUID(),
    });
  }

  createTransfer(request: PaymentTransferRequest, idempotencyKey: string): Observable<PaymentStatusResponse> {
    return this.http.post<PaymentStatusResponse>(this.paymentsEndpoint, request, {
      headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
    });
  }

  getPaymentStatus(paymentId: string): Observable<PaymentStatusResponse> {
    return this.http.get<PaymentStatusResponse>(`${this.paymentsEndpoint}/${encodeURIComponent(paymentId)}`);
  }

  findPaymentByIdempotencyKey(idempotencyKey: string): Observable<PaymentStatusResponse> {
    return this.http.get<PaymentStatusResponse>(`${this.paymentsEndpoint}/by-idempotency-key`, {
      headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }),
    });
  }

  getPaymentHistory(page: number, pageSize: number): Observable<PaymentHistoryResponse> {
    const params = new HttpParams()
      .set('page', page)
      .set('pageSize', pageSize);
    return this.http.get<PaymentHistoryResponse>(this.paymentsEndpoint, { params });
  }
}
