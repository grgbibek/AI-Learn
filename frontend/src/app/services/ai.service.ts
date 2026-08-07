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
  private askAbortController: AbortController | null = null;

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

  // Streams the answer token-by-token over Server-Sent Events instead of waiting for the full response.
  askKnowledgeBase(question: string, topK = 3): void {
    this.askLoading.set(true);
    this.askError.set(null);
    this.askResult.set({ question, answer: '', rerankMethod: '', sources: [] });

    this.askAbortController = new AbortController();

    this.streamAsk(question, topK, this.askAbortController.signal).catch((err: unknown) => {
      if (err instanceof DOMException && err.name === 'AbortError') {
        // User clicked Stop - not a real failure, keep whatever partial answer already streamed in.
        this.askLoading.set(false);
        return;
      }
      console.error('Failed to query knowledge base', err);
      this.askError.set('Ask failed. Ingest a document first, and make sure the backend/Ollama is running.');
      this.askLoading.set(false);
    });
  }

  // Cancels the in-progress stream: aborting the fetch closes the connection, which cancels the
  // backend's CancellationToken too, stopping both our API and the underlying Ollama generation.
  stopAsk(): void {
    this.askAbortController?.abort();
  }

  private async streamAsk(question: string, topK: number, signal: AbortSignal): Promise<void> {
    const response = await fetch(`${this.ragUrl}/ask-stream`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ question, topK }),
      signal
    });

    if (!response.ok || !response.body) {
      throw new Error(`Request failed with status ${response.status}`);
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      // SSE frames are separated by a blank line; the last split piece may be incomplete, so hold it back.
      const frames = buffer.split('\n\n');
      buffer = frames.pop() ?? '';

      for (const frame of frames) {
        this.handleSseFrame(frame);
      }
    }

    this.askLoading.set(false);
  }

  private handleSseFrame(frame: string): void {
    let eventName = 'message';
    let data = '';

    for (const line of frame.split('\n')) {
      if (line.startsWith('event:')) eventName = line.slice(6).trim();
      else if (line.startsWith('data:')) data += line.slice(5).trim();
    }
    if (!data) return;

    const payload = JSON.parse(data);

    if (eventName === 'sources') {
      this.askResult.update(current => current && {
        ...current,
        rerankMethod: payload.rerankMethod,
        sources: payload.sources
      });
    } else if (eventName === 'token') {
      this.askResult.update(current => current && {
        ...current,
        answer: current.answer + payload.text
      });
    }
  }
}
