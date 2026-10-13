import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { API_BASE_URL } from '../api/api.config';
import {
  LoginRequest,
  RegisterRequest,
  TokenResponse,
  UserResponse,
} from '../../features/authentication/models/auth.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly accessTokenKey = 'bank-system.access-token';
  private readonly authSessionKey = 'bank-system.auth-session';
  private readonly authenticated = signal(this.readAccessToken() !== null);

  readonly isAuthenticated = computed(() => this.authenticated());

  login(request: LoginRequest): Observable<TokenResponse> {
    return this.http
      .post<TokenResponse>(`${API_BASE_URL}/auth/login`, request)
      .pipe(tap((response) => this.saveAccessToken(response.accessToken)));
  }

  register(request: RegisterRequest): Observable<UserResponse> {
    return this.http.post<UserResponse>(`${API_BASE_URL}/auth/register`, request);
  }

  getAccessToken(): string | null {
    return this.readAccessToken();
  }

  getSessionId(): string {
    let sessionId = sessionStorage.getItem(this.authSessionKey);
    if (!sessionId) {
      sessionId = crypto.randomUUID();
      sessionStorage.setItem(this.authSessionKey, sessionId);
    }
    return sessionId;
  }

  logout(): void {
    sessionStorage.removeItem(this.accessTokenKey);
    sessionStorage.removeItem(this.authSessionKey);
    this.authenticated.set(false);
  }

  private saveAccessToken(accessToken: string): void {
    sessionStorage.setItem(this.accessTokenKey, accessToken);
    sessionStorage.setItem(this.authSessionKey, crypto.randomUUID());
    this.authenticated.set(true);
  }

  private readAccessToken(): string | null {
    return sessionStorage.getItem(this.accessTokenKey);
  }
}
