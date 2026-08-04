import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import {
  SubtaskAnalysisResponse,
  WorkloadAssistantResponse,
  SemanticSimilarityResponse
} from '../models/ai.model';

@Injectable({
  providedIn: 'root'
})
export class AiService {
  private http = inject(HttpClient);
  private apiUrl = 'http://localhost:5198/api/ai';

  readonly loading = signal<boolean>(false);
  readonly error = signal<string | null>(null);

  // Structured subtask analysis result, keyed by open work item card
  readonly analysis = signal<SubtaskAnalysisResponse | null>(null);

  // Natural-language workload assistant (native C# tool calling)
  readonly assistantLoading = signal<boolean>(false);
  readonly assistantResponse = signal<WorkloadAssistantResponse | null>(null);

  suggestSubtasks(workItemId: number): void {
    this.loading.set(true);
    this.error.set(null);
    this.analysis.set(null);

    this.http.post<SubtaskAnalysisResponse>(`${this.apiUrl}/structured-analysis/${workItemId}`, {}).subscribe({
      next: (result) => {
        this.analysis.set(result);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        console.error('Failed to get AI subtask analysis', err);
        this.error.set('AI assistant is unavailable. Is the backend/Ollama running?');
        this.loading.set(false);
      }
    });
  }

  clearAnalysis(): void {
    this.analysis.set(null);
    this.error.set(null);
  }

  askWorkloadAssistant(prompt: string): void {
    this.assistantLoading.set(true);
    this.error.set(null);
    this.assistantResponse.set(null);

    this.http.post<WorkloadAssistantResponse>(`${this.apiUrl}/workload-assistant`, { userPrompt: prompt }).subscribe({
      next: (result) => {
        this.assistantResponse.set(result);
        this.assistantLoading.set(false);
      },
      error: (err: unknown) => {
        console.error('Failed to query workload assistant', err);
        this.error.set('AI assistant is unavailable. Is the backend/Ollama running?');
        this.assistantLoading.set(false);
      }
    });
  }

  compareSemanticSimilarity(text1: string, text2: string) {
    return this.http.post<SemanticSimilarityResponse>(`${this.apiUrl}/semantic-similarity`, { text1, text2 });
  }
}
