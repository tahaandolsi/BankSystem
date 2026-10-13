import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { AuthService } from './auth.service';

/** Ajoute le JWT aux appels qui suivent une connexion réussie. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const accessToken = inject(AuthService).getAccessToken();

  if (!accessToken || request.url.includes('/auth/')) {
    return next(request);
  }

  return next(
    request.clone({
      setHeaders: { Authorization: `Bearer ${accessToken}` },
    }),
  );
};
