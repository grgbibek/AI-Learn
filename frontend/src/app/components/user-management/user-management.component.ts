import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AppUser, CreateUserRequest, UserService } from '../../services/user.service';

@Component({
  selector: 'app-user-management',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <section class="users-panel">
      <div class="section-header">
        <div>
          <p class="eyebrow">Administration</p>
          <h2>User portal</h2>
        </div>
        <button type="button" class="ghost" (click)="loadUsers()">Refresh</button>
      </div>

      <form class="user-form" [formGroup]="form" (ngSubmit)="createUser()">
        <input placeholder="Username" formControlName="userName" />
        <input placeholder="Email" formControlName="email" />
        <input placeholder="Password" type="password" formControlName="password" />
        <select formControlName="role">
          <option value="User">User</option>
          <option value="Admin">Admin</option>
        </select>
        <input type="number" min="1" title="Daily request limit" formControlName="dailyAiRequestLimit" />
        <input type="number" min="1" title="Daily token limit" formControlName="dailyAiTokenLimit" />
        <label class="checkbox"><input type="checkbox" formControlName="isActive" /> Active</label>
        <button type="submit" [disabled]="form.invalid || loading()">Create user</button>
      </form>

      @if (message()) { <p class="message">{{ message() }}</p> }
      @if (error()) { <p class="error">{{ error() }}</p> }

      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>User</th>
              <th>Email</th>
              <th>Role</th>
              <th>Daily requests</th>
              <th>Daily tokens</th>
              <th>Usage today</th>
              <th>Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            @for (user of users(); track user.id) {
              <tr>
                <td>{{ user.userName }}</td>
                <td><input [value]="user.email" #email /></td>
                <td>
                  <select [value]="user.role" #role>
                    <option value="User">User</option>
                    <option value="Admin">Admin</option>
                  </select>
                </td>
                <td><input type="number" min="1" [value]="user.dailyAiRequestLimit" #limit /></td>
                <td><input type="number" min="1" [value]="user.dailyAiTokenLimit" #tokenLimit /></td>
                <td>
                  <div class="usage-meter">
                    <span>{{ user.usageToday.requestsUsed }} / {{ user.usageToday.requestLimit }} req</span>
                    <div class="bar"><span [style.width.%]="usagePercent(user.usageToday.requestsUsed, user.usageToday.requestLimit)"></span></div>
                    <span>{{ user.usageToday.tokensUsed }} / {{ user.usageToday.tokenLimit }} tokens</span>
                    <div class="bar token"><span [style.width.%]="usagePercent(user.usageToday.tokensUsed, user.usageToday.tokenLimit)"></span></div>
                    @if (user.usageToday.budgetBlocks > 0) {
                      <strong>{{ user.usageToday.budgetBlocks }} blocked</strong>
                    }
                  </div>
                </td>
                <td>
                  <label class="checkbox"><input type="checkbox" [checked]="user.isActive" #active /> Active</label>
                </td>
                <td><button type="button" class="ghost" (click)="updateUser(user, email.value, role.value, limit.value, tokenLimit.value, active.checked)">Save</button></td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
  styles: [`
    .users-panel { background: #1e293b; border: 1px solid #334155; border-radius: 8px; padding: 1rem; display: grid; gap: 1rem; }
    .section-header { display: flex; justify-content: space-between; align-items: center; gap: 1rem; }
    h2 { margin: 0.1rem 0 0; }
    .eyebrow { color: #93c5fd; margin: 0; font-size: 0.75rem; font-weight: 800; letter-spacing: 0.08em; text-transform: uppercase; }
    .user-form { display: grid; grid-template-columns: repeat(8, minmax(0, 1fr)); gap: 0.6rem; align-items: center; }
    input, select { width: 100%; box-sizing: border-box; background: #0f172a; color: #f8fafc; border: 1px solid #334155; border-radius: 6px; padding: 0.55rem; }
    button { border: 0; border-radius: 6px; padding: 0.6rem 0.75rem; background: #3b82f6; color: white; font-weight: 700; cursor: pointer; }
    button:disabled { opacity: 0.5; cursor: not-allowed; }
    .ghost { background: #0f172a; color: #bfdbfe; border: 1px solid #334155; }
    .checkbox { display: flex; align-items: center; gap: 0.35rem; color: #cbd5e1; white-space: nowrap; }
    .checkbox input { width: auto; }
    .table-wrap { overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; }
    th, td { border-bottom: 1px solid #334155; padding: 0.65rem; text-align: left; color: #e2e8f0; }
    th { color: #93c5fd; font-size: 0.78rem; text-transform: uppercase; letter-spacing: 0.05em; }
    .usage-meter { display: grid; gap: 0.25rem; min-width: 170px; font-size: 0.78rem; color: #cbd5e1; }
    .usage-meter strong { color: #fecaca; font-weight: 700; }
    .bar { height: 6px; background: #334155; border-radius: 999px; overflow: hidden; }
    .bar span { display: block; height: 100%; background: #38bdf8; border-radius: inherit; }
    .bar.token span { background: #a855f7; }
    .message { color: #bbf7d0; margin: 0; }
    .error { color: #fecaca; margin: 0; }
    @media (max-width: 900px) { .user-form { grid-template-columns: 1fr 1fr; } }
  `]
})
export class UserManagementComponent {
  private fb = inject(FormBuilder);
  private userService = inject(UserService);

  readonly users = signal<AppUser[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly message = signal<string | null>(null);
  readonly form = this.fb.nonNullable.group({
    userName: ['', [Validators.required]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    role: ['User' as 'User' | 'Admin', [Validators.required]],
    dailyAiRequestLimit: [100, [Validators.required, Validators.min(1)]],
    dailyAiTokenLimit: [100000, [Validators.required, Validators.min(1)]],
    isActive: [true]
  });

  constructor() {
    this.loadUsers();
  }

  loadUsers(): void {
    this.loading.set(true);
    this.error.set(null);
    this.userService.getUsers().subscribe({
      next: users => { this.users.set(users); this.loading.set(false); },
      error: () => { this.error.set('Could not load users.'); this.loading.set(false); }
    });
  }

  createUser(): void {
    if (this.form.invalid) return;

    this.loading.set(true);
    this.error.set(null);
    this.message.set(null);
    const request = this.form.getRawValue() as CreateUserRequest;
    this.userService.createUser(request).subscribe({
      next: () => {
        this.message.set('User created.');
        this.form.reset({ role: 'User', dailyAiRequestLimit: 100, dailyAiTokenLimit: 100000, isActive: true, userName: '', email: '', password: '' });
        this.loadUsers();
      },
      error: () => { this.error.set('Could not create user.'); this.loading.set(false); }
    });
  }

  updateUser(user: AppUser, email: string, role: string, dailyLimit: string, dailyTokenLimit: string, isActive: boolean): void {
    this.loading.set(true);
    this.error.set(null);
    this.message.set(null);
    this.userService.updateUser(user.id, {
      email,
      password: null,
      role: role === 'Admin' ? 'Admin' : 'User',
      dailyAiRequestLimit: Math.max(1, Number(dailyLimit)),
      dailyAiTokenLimit: Math.max(1, Number(dailyTokenLimit)),
      isActive
    }).subscribe({
      next: () => { this.message.set('User updated.'); this.loadUsers(); },
      error: () => { this.error.set('Could not update user.'); this.loading.set(false); }
    });
  }

  usagePercent(used: number, limit: number): number {
    if (limit <= 0) return 0;
    return Math.min(100, Math.round((used / limit) * 100));
  }
}