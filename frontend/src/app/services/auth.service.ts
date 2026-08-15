import { Injectable, computed, signal } from '@angular/core';

export interface AuthSession {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
  userName: string;
  role: string;
  dailyAiRequestLimit: number;
  dailyAiTokenLimit: number;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly sessionKey = 'taskflow.authSession';
  private readonly loginUrl = 'http://localhost:5198/api/auth/login';
  private readonly sessionSignal = signal<AuthSession | null>(this.loadStoredSession());

  readonly session = this.sessionSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.sessionSignal() !== null);
  readonly isAdmin = computed(() => this.sessionSignal()?.role === 'Admin');

  login(userName: string, password: string): Promise<AuthSession> {
    return fetch(this.loginUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userName, password })
    })
      .then(async response => {
        if (!response.ok) {
          throw new Error(response.status === 401 ? 'Invalid username or password.' : `Login failed with status ${response.status}.`);
        }

        return await response.json() as AuthSession;
      })
      .then(session => {
        this.storeSession(session);
        return session;
      });
  }

  getAccessToken(): Promise<string> {
    const session = this.sessionSignal();
    if (!session) {
      return Promise.reject(new Error('Not authenticated.'));
    }

    return Promise.resolve(session.accessToken);
  }

  clearToken(): void {
    this.sessionSignal.set(null);
    localStorage.removeItem(this.sessionKey);
  }

  logout(): void {
    this.clearToken();
  }

  private storeSession(session: AuthSession): void {
    this.sessionSignal.set(session);
    localStorage.setItem(this.sessionKey, JSON.stringify(session));
  }

  private loadStoredSession(): AuthSession | null {
    const raw = localStorage.getItem(this.sessionKey);
    if (!raw) return null;

    try {
      const session = JSON.parse(raw) as AuthSession;
      return new Date(session.expiresAt).getTime() > Date.now() ? session : null;
    } catch {
      localStorage.removeItem(this.sessionKey);
      return null;
    }
  }
}