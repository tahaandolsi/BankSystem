import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../../core/api/api.config';
import { Account, CreateAccountRequest } from '../models/account.models';

@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly http = inject(HttpClient);
  private readonly endpoint = `${API_BASE_URL}/accounts`;

  getMyAccounts(): Observable<Account[]> {
    return this.http.get<Account[]>(`${this.endpoint}/customer/me`);
  }

  getById(accountId: string): Observable<Account> {
    return this.http.get<Account>(`${this.endpoint}/${accountId}`);
  }

  create(request: CreateAccountRequest): Observable<Account> {
    return this.http.post<Account>(this.endpoint, request);
  }
}
