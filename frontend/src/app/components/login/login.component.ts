import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <section class="login-shell">
      <form class="login-panel" [formGroup]="form" (ngSubmit)="submit()">
        <div>
          <p class="eyebrow">TaskFlow access</p>
          <h1>Sign in</h1>
          <p class="hint">Use the seeded local admin account or a user created by an admin.</p>
        </div>

        <label>
          Username or email
          <input type="text" formControlName="userName" autocomplete="username" />
        </label>

        <label>
          Password
          <input type="password" formControlName="password" autocomplete="current-password" />
        </label>

        @if (error()) {
          <p class="error">{{ error() }}</p>
        }

        <button type="submit" [disabled]="form.invalid || loading()">
          {{ loading() ? 'Signing in...' : 'Sign in' }}
        </button>

        <p class="hint small">Local default: admin / Admin123!</p>
      </form>
    </section>
  `,
  styles: [`
    .login-shell {
      min-height: 100vh;
      display: grid;
      place-items: center;
      background: #0f172a;
      color: #f8fafc;
      padding: 1rem;
    }
    .login-panel {
      width: min(420px, 100%);
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 1.5rem;
      display: grid;
      gap: 1rem;
      box-shadow: 0 24px 80px rgba(2, 6, 23, 0.45);
    }
    h1 { margin: 0.15rem 0; font-size: 1.8rem; }
    .eyebrow { color: #93c5fd; margin: 0; font-size: 0.8rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.08em; }
    .hint { color: #94a3b8; margin: 0; line-height: 1.45; }
    .small { font-size: 0.8rem; }
    label { display: grid; gap: 0.4rem; color: #cbd5e1; font-weight: 600; }
    input {
      background: #0f172a;
      border: 1px solid #334155;
      color: #f8fafc;
      border-radius: 6px;
      padding: 0.75rem;
      font: inherit;
    }
    input:focus { outline: 2px solid #3b82f6; border-color: transparent; }
    button {
      border: 0;
      border-radius: 6px;
      padding: 0.8rem 1rem;
      background: #3b82f6;
      color: white;
      font-weight: 700;
      cursor: pointer;
    }
    button:disabled { opacity: 0.55; cursor: not-allowed; }
    .error { margin: 0; color: #fecaca; background: rgba(239, 68, 68, 0.14); border: 1px solid rgba(239, 68, 68, 0.35); border-radius: 6px; padding: 0.65rem; }
  `]
})
export class LoginComponent {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly form = this.fb.nonNullable.group({
    userName: ['admin', [Validators.required]],
    password: ['Admin123!', [Validators.required]]
  });

  submit(): void {
    if (this.form.invalid) return;

    this.loading.set(true);
    this.error.set(null);
    const { userName, password } = this.form.getRawValue();

    this.auth.login(userName, password)
      .catch((err: unknown) => this.error.set(err instanceof Error ? err.message : 'Login failed.'))
      .finally(() => this.loading.set(false));
  }
}