import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import {
  SubtaskAnalysisResponse,
  WorkloadAssistantResponse,
  SemanticSimilarityResponse,
  IngestDocumentResponse,
  AskKnowledgeBaseResponse
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

  // RAG Knowledge Base (Phase 3, Lesson 2): ingest documents, then ask grounded questions.
  readonly ragUrl = 'http://localhost:5198/api/rag';

  readonly ingestLoading = signal<boolean>(false);
  readonly ingestResult = signal<IngestDocumentResponse | null>(null);
  readonly ingestError = signal<string | null>(null);

  readonly askLoading = signal<boolean>(false);
  readonly askResult = signal<AskKnowledgeBaseResponse | null>(null);
  readonly askError = signal<string | null>(null);

  ingestDocument(title: string, content: string): void {
    this.ingestLoading.set(true);
    this.ingestError.set(null);
    this.ingestResult.set(null);

    this.http.post<IngestDocumentResponse>(`${this.ragUrl}/ingest`, { title, content }).subscribe({
      next: (result) => {
        this.ingestResult.set(result);
        this.ingestLoading.set(false);
      },
      error: (err: unknown) => {
        console.error('Failed to ingest document', err);
        this.ingestError.set('Ingest failed. Is the backend/Ollama running?');
        this.ingestLoading.set(false);
      }
    });
  }

  askKnowledgeBase(question: string, topK = 3): void {
    this.askLoading.set(true);
    this.askError.set(null);
    this.askResult.set(null);

    this.http.post<AskKnowledgeBaseResponse>(`${this.ragUrl}/ask`, { question, topK }).subscribe({
      next: (result) => {
        this.askResult.set(result);
        this.askLoading.set(false);
      },
      error: (err: unknown) => {
        console.error('Failed to query knowledge base', err);
        this.askError.set('Ask failed. Ingest a document first, and make sure the backend/Ollama is running.');
        this.askLoading.set(false);
      }
    });
  }
}
