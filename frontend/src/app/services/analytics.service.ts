import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';

export interface StatusDistribution {
  todo: number;
  inProgress: number;
  done: number;
}

export interface PriorityDistribution {
  low: number;
  medium: number;
  high: number;
  critical: number;
}

export interface AgentPipelineMetrics {
  totalRuns: number;
  approvedRuns: number;
  rejectedRuns: number;
  approvalRate: number;
}

export interface KnowledgeBaseMetrics {
  totalDocuments: number;
  totalChunks: number;
}

export interface AnalyticsMetrics {
  totalWorkItems: number;
  completedWorkItems: number;
  pendingWorkItems: number;
  completionRate: number;
  statusDistribution: StatusDistribution;
  priorityDistribution: PriorityDistribution;
  agentMetrics: AgentPipelineMetrics;
  knowledgeBaseMetrics: KnowledgeBaseMetrics;
}

@Injectable({
  providedIn: 'root'
})
export class AnalyticsService {
  private http = inject(HttpClient);
  private apiUrl = 'http://localhost:5198/api/analytics';

  readonly loading = signal<boolean>(false);
  readonly error = signal<string | null>(null);
  readonly metrics = signal<AnalyticsMetrics | null>(null);

  loadMetrics(): void {
    this.loading.set(true);
    this.error.set(null);

    this.http.get<AnalyticsMetrics>(`${this.apiUrl}/metrics`).subscribe({
      next: (data) => {
        this.metrics.set(data);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        console.error('Failed to load analytics metrics', err);
        this.error.set('Failed to connect to backend analytics API.');
        this.loading.set(false);
      }
    });
  }
}
