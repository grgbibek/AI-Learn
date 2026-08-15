import { Component, inject, signal } from '@angular/core';
import { TaskBoardComponent } from './components/task-board/task-board.component';
import { KnowledgeBaseComponent } from './components/knowledge-base/knowledge-base.component';
import { AgentPipelineComponent } from './components/agent-pipeline/agent-pipeline.component';
import { AnalyticsDashboardComponent } from './components/analytics-dashboard/analytics-dashboard.component';
import { LoginComponent } from './components/login/login.component';
import { UserManagementComponent } from './components/user-management/user-management.component';
import { AuthService } from './services/auth.service';

export type ActiveTab = 'board' | 'analytics' | 'rag' | 'agent' | 'users' | 'all';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    TaskBoardComponent,
    KnowledgeBaseComponent,
    AgentPipelineComponent,
    AnalyticsDashboardComponent,
    LoginComponent,
    UserManagementComponent
  ],
  template: `
    @if (!auth.isAuthenticated()) {
      <app-login></app-login>
    } @else {
      <div class="app-shell">
      <header class="navbar">
        <div class="nav-content">
          <div class="brand">
            <span class="logo">⚡</span>
            <h2>TaskFlow <span class="badge">.NET 10 + Angular 19</span></h2>
          </div>
          
          <nav class="nav-tabs">
            <button 
              [class.active]="activeTab() === 'board'" 
              (click)="activeTab.set('board')" 
              class="tab-btn">
              📋 Task Board
            </button>
            <button 
              [class.active]="activeTab() === 'analytics'" 
              (click)="activeTab.set('analytics')" 
              class="tab-btn">
              📊 Analytics
            </button>
            <button 
              [class.active]="activeTab() === 'rag'" 
              (click)="activeTab.set('rag')" 
              class="tab-btn">
              📚 Knowledge Base
            </button>
            <button 
              [class.active]="activeTab() === 'agent'" 
              (click)="activeTab.set('agent')" 
              class="tab-btn">
              🤖 Multi-Agent
            </button>
            @if (auth.isAdmin()) {
              <button
                [class.active]="activeTab() === 'users'"
                (click)="activeTab.set('users')"
                class="tab-btn">
                👥 Users
              </button>
            }
            <button 
              [class.active]="activeTab() === 'all'" 
              (click)="activeTab.set('all')" 
              class="tab-btn tab-btn-all">
              🌐 View All
            </button>
          </nav>

          <div class="resource-links" aria-label="Local AI tools">
            <a href="http://localhost:11434" target="_blank" rel="noopener noreferrer" class="resource-link">
              Ollama
            </a>
            <a href="http://localhost:18888" target="_blank" rel="noopener noreferrer" class="resource-link">
              Aspire Dashboard
            </a>
          </div>

          <p class="subtitle">Phase 4 — Streaming AI UI &amp; Analytics</p>

          <div class="session-chip">
            <span>{{ auth.session()?.userName }}</span>
            <strong>{{ auth.session()?.role }}</strong>
            <button type="button" (click)="logout()">Logout</button>
          </div>
        </div>
      </header>

      <main class="main-content">
        @if (activeTab() === 'analytics') {
          <app-analytics-dashboard></app-analytics-dashboard>
        }

        @if (activeTab() === 'board') {
          <app-task-board></app-task-board>
        }

        @if (activeTab() === 'rag') {
          <app-knowledge-base></app-knowledge-base>
        }

        @if (activeTab() === 'agent') {
          <app-agent-pipeline></app-agent-pipeline>
        }

        @if (activeTab() === 'users' && auth.isAdmin()) {
          <app-user-management></app-user-management>
        }

        @if (activeTab() === 'all') {
          <app-analytics-dashboard></app-analytics-dashboard>
          <app-task-board></app-task-board>
          <app-knowledge-base></app-knowledge-base>
          <app-agent-pipeline></app-agent-pipeline>
          @if (auth.isAdmin()) {
            <app-user-management></app-user-management>
          }
        }
      </main>
    </div>
    }
  `,
  styles: [`
    .app-shell {
      min-height: 100vh;
      background: #0f172a;
      color: #f8fafc;
      font-family: 'Segoe UI', system-ui, -apple-system, sans-serif;
    }
    .navbar {
      background: #1e293b;
      border-bottom: 1px solid #334155;
      padding: 0.75rem 2rem;
      position: sticky;
      top: 0;
      z-index: 100;
    }
    .nav-content {
      max-width: 1200px;
      margin: 0 auto;
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: 1rem;
    }
    .brand {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }
    .brand h2 {
      font-size: 1.25rem;
      margin: 0;
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .logo {
      font-size: 1.5rem;
    }
    .badge {
      font-size: 0.7rem;
      background: #3b82f6;
      color: white;
      padding: 0.2rem 0.5rem;
      border-radius: 6px;
      font-weight: 500;
    }
    .nav-tabs {
      display: flex;
      gap: 0.5rem;
      background: #0f172a;
      padding: 0.25rem;
      border-radius: 8px;
      border: 1px solid #334155;
    }
    .tab-btn {
      background: transparent;
      border: none;
      color: #94a3b8;
      padding: 0.45rem 0.85rem;
      border-radius: 6px;
      font-size: 0.85rem;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s ease;
    }
    .tab-btn:hover {
      color: #f8fafc;
      background: rgba(255, 255, 255, 0.05);
    }
    .tab-btn.active {
      background: #3b82f6;
      color: white;
    }
    .tab-btn-all.active {
      background: #8b5cf6;
    }
    .resource-links {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .resource-link {
      color: #93c5fd;
      border: 1px solid #334155;
      background: #0f172a;
      border-radius: 6px;
      padding: 0.4rem 0.65rem;
      text-decoration: none;
      font-size: 0.8rem;
      font-weight: 600;
      transition: all 0.2s ease;
    }
    .resource-link:hover {
      color: #f8fafc;
      border-color: #3b82f6;
      background: rgba(59, 130, 246, 0.14);
    }
    .subtitle {
      color: #64748b;
      font-size: 0.8rem;
      margin: 0;
    }
    .session-chip {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      color: #cbd5e1;
      background: #0f172a;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 0.35rem 0.5rem;
      font-size: 0.8rem;
    }
    .session-chip strong {
      color: #93c5fd;
    }
    .session-chip button {
      border: 0;
      border-radius: 6px;
      background: #334155;
      color: #f8fafc;
      padding: 0.3rem 0.55rem;
      cursor: pointer;
      font-weight: 700;
    }
    .main-content {
      max-width: 1200px;
      margin: 0 auto;
      padding: 1.5rem 1rem;
    }
  `]
})
export class AppComponent {
  protected readonly auth = inject(AuthService);
  readonly activeTab = signal<ActiveTab>('board');

  logout(): void {
    this.auth.logout();
    this.activeTab.set('board');
  }
}
