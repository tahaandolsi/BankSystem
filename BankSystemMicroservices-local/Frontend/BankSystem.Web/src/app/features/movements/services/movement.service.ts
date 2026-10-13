import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../../core/api/api.config';
import { Movement, MovementFilters } from '../models/movement.models';

@Injectable({ providedIn: 'root' })
export class MovementService {
  private readonly http = inject(HttpClient);
  private readonly endpoint = `${API_BASE_URL}/movements`;

  getByAccount(accountId: string, filters: MovementFilters = {}): Observable<Movement[]> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filters)) {
      if (value !== undefined && value !== '') {
        params = params.set(key, String(value));
      }
    }

    return this.http.get<Movement[]>(`${this.endpoint}/account/${accountId}`, { params });
  }
}
