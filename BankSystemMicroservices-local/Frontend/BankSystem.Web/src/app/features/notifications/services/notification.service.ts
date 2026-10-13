import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../../../core/api/api.config';
import { Notification } from '../models/notification.models';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);

  getMine(): Observable<Notification[]> {
    return this.http.get<Notification[]>(`${API_BASE_URL}/notifications/me`);
  }
}
