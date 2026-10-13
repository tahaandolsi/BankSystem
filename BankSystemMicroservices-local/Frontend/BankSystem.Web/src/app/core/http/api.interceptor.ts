import { HttpInterceptorFn } from '@angular/common/http';

/** Emplacement reserve aux comportements HTTP communs. */
export const apiInterceptor: HttpInterceptorFn = (request, next) => next(request);
