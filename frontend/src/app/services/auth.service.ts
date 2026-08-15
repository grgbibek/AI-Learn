import { Injectable } from '@angular/core';

interface DevTokenResponse {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
  userName: string;
  role: string;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly tokenKey = 'taskflow.devAccessToken';
  private readonly devTokenUrl = 'http://localhost:5198/api/auth/dev-token';
  private accessToken: string | null = localStorage.getItem(this.tokenKey);
  private pendingToken: Promise<string> | null = null;

  getAccessToken(): Promise<string> {
    if (this.accessToken) {
      return Promise.resolve(this.accessToken);
    }

    this.pendingToken ??= fetch(this.devTokenUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userName: 'local-admin', role: 'Admin' })
    })
      .then(async response => {
        if (!response.ok) {
          throw new Error(`Dev token request failed with status ${response.status}`);
        }

        return await response.json() as DevTokenResponse;
      })
      .then(token => {
        this.accessToken = token.accessToken;
        localStorage.setItem(this.tokenKey, token.accessToken);
        return token.accessToken;
      })
      .finally(() => {
        this.pendingToken = null;
      });

    return this.pendingToken;
  }

  clearToken(): void {
    this.accessToken = null;
    localStorage.removeItem(this.tokenKey);
  }
}