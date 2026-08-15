import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('http://localhost:5198/api/') || req.url.includes('/api/auth/dev-token')) {
    return next(req);
  }

  const auth = inject(AuthService);
  const withBearerToken = (token: string) => req.clone({
    setHeaders: {
      Authorization: `Bearer ${token}`
    }
  });

  return from(auth.getAccessToken()).pipe(
    switchMap(token => next(withBearerToken(token)).pipe(
      catchError(error => {
        if (error.status !== 401) {
          return throwError(() => error);
        }

        auth.clearToken();
        return from(auth.getAccessToken()).pipe(
          switchMap(refreshedToken => next(withBearerToken(refreshedToken)))
        );
      })
    ))
  );
};