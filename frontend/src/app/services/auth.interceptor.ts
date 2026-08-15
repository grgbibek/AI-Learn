import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { from, switchMap } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('http://localhost:5198/api/') || req.url.includes('/api/auth/dev-token')) {
    return next(req);
  }

  const auth = inject(AuthService);
  return from(auth.getAccessToken()).pipe(
    switchMap(token => next(req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`
      }
    })))
  );
};