import { Component } from '@angular/core';
import { TaskBoardComponent } from './components/task-board/task-board.component';
import { KnowledgeBaseComponent } from './components/knowledge-base/knowledge-base.component';
import { AgentPipelineComponent } from './components/agent-pipeline/agent-pipeline.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [TaskBoardComponent, KnowledgeBaseComponent, AgentPipelineComponent],
  template: `
    <div class="app-shell">
      <header class="navbar">
        <div class="nav-content">
          <div class="brand">
            <span class="logo">⚡</span>
            <h2>TaskFlow <span class="badge">.NET 10 + Angular 19</span></h2>
          </div>
          <p class="subtitle">AI Agent Workflow Demonstration</p>
        </div>
      </header>
      <main>
        <app-knowledge-base></app-knowledge-base>
        <app-agent-pipeline></app-agent-pipeline>
        <app-task-board></app-task-board>
      </main>
    </div>
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
      padding: 1rem 2rem;
    }
    .nav-content {
      max-width: 1200px;
      margin: 0 auto;
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .brand {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }
    .brand h2 {
      font-size: 1.35rem;
      margin: 0;
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .logo {
      font-size: 1.5rem;
    }
    .badge {
      font-size: 0.75rem;
      background: #3b82f6;
      color: white;
      padding: 0.2rem 0.5rem;
      border-radius: 6px;
      font-weight: 500;
    }
    .subtitle {
      color: #94a3b8;
      font-size: 0.875rem;
      margin: 0;
    }
  `]
})
export class AppComponent {}
