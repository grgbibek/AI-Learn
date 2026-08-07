import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { PlanFeatureResponse, AgentAuditLogEntry } from '../models/agent-pipeline.model';

// Multi-Agent Orchestration (Phase 5): Planner -> Developer -> Reviewer pipeline,
// with a durable, queryable audit trail of every attempt (approved or rejected).
@Injectable({
  providedIn: 'root'
})
export class AgentPipelineService {
  private http = inject(HttpClient);
  private apiUrl = 'http://localhost:5198/api/agents';

  readonly planLoading = signal<boolean>(false);
  readonly planResult = signal<PlanFeatureResponse | null>(null);
  readonly planError = signal<string | null>(null);

  readonly auditLog = signal<AgentAuditLogEntry[]>([]);
  readonly auditLoading = signal<boolean>(false);

  planFeature(featureRequest: string): void {
    this.planLoading.set(true);
    this.planError.set(null);
    this.planResult.set(null);

    this.http.post<PlanFeatureResponse>(`${this.apiUrl}/plan-feature`, { featureRequest }).subscribe({
      next: (result) => {
        this.planResult.set(result);
        this.planLoading.set(false);
        this.loadAuditLog();
      },
      error: (err: unknown) => {
        console.error('Failed to plan feature', err);
        this.planError.set('Planning failed. Is the backend/Ollama running?');
        this.planLoading.set(false);
      }
    });
  }

  loadAuditLog(take = 20): void {
    this.auditLoading.set(true);

    this.http.get<AgentAuditLogEntry[]>(`${this.apiUrl}/audit-log?take=${take}`).subscribe({
      next: (result) => {
        this.auditLog.set(result);
        this.auditLoading.set(false);
      },
      error: (err: unknown) => {
        console.error('Failed to load agent audit log', err);
        this.auditLoading.set(false);
      }
    });
  }
}
