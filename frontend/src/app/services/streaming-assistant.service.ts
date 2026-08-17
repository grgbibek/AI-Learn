import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from './auth.service';
import {
  AiConversationMessageResponse,
  AiConversationResponse,
  AiConversationSummaryResponse,
  StreamingAssistantStatus
} from '../models/ai.model';

@Injectable({
  providedIn: 'root'
})
export class StreamingAssistantService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly apiUrl = 'http://localhost:5198/api/ai';
  private streamAbortController: AbortController | null = null;

  readonly conversations = signal<AiConversationSummaryResponse[]>([]);
  readonly activeConversation = signal<AiConversationResponse | null>(null);
  readonly status = signal<StreamingAssistantStatus>('idle');
  readonly error = signal<string | null>(null);

  readonly isStreaming = computed(() => this.status() === 'streaming');
  readonly isLoading = computed(() => this.status() === 'loadingConversations' || this.status() === 'loadingConversation');
  readonly messages = computed(() => this.activeConversation()?.messages ?? []);

  async loadConversations(): Promise<void> {
    this.status.set('loadingConversations');
    this.error.set(null);

    try {
      const conversations = await firstValueFrom(
        this.http.get<AiConversationSummaryResponse[]>(`${this.apiUrl}/conversations`));
      this.conversations.set(conversations);

      const activeId = this.activeConversation()?.id;
      if (activeId && conversations.some(conversation => conversation.id === activeId)) {
        await this.loadConversation(activeId);
      } else if (conversations.length > 0) {
        await this.loadConversation(conversations[0].id);
      } else {
        this.activeConversation.set(null);
        this.status.set('idle');
      }
    } catch (err: unknown) {
      console.error('Failed to load AI conversations', err);
      this.error.set('Unable to load AI conversations. Is the backend running?');
      this.status.set('error');
    }
  }

  async createConversation(title?: string): Promise<AiConversationResponse> {
    this.error.set(null);
    const conversation = await firstValueFrom(
      this.http.post<AiConversationResponse>(`${this.apiUrl}/conversations`, { title: title ?? null }));

    this.conversations.update(current => [
      this.toSummary(conversation),
      ...current.filter(item => item.id !== conversation.id)
    ]);
    this.activeConversation.set(conversation);
    this.status.set('idle');

    return conversation;
  }

  async loadConversation(id: number): Promise<void> {
    if (this.isStreaming()) {
      return;
    }

    this.status.set('loadingConversation');
    this.error.set(null);

    try {
      const conversation = await this.fetchConversation(id);
      this.activeConversation.set(conversation);
      this.status.set('idle');
    } catch (err: unknown) {
      console.error('Failed to load AI conversation', err);
      this.error.set('Unable to load that conversation.');
      this.status.set('error');
    }
  }

  async deleteConversation(id: number): Promise<void> {
    if (this.isStreaming()) {
      this.stopStreaming();
    }

    this.error.set(null);
    await firstValueFrom(this.http.delete<void>(`${this.apiUrl}/conversations/${id}`));

    const remaining = this.conversations().filter(conversation => conversation.id !== id);
    this.conversations.set(remaining);
    if (this.activeConversation()?.id === id) {
      this.activeConversation.set(null);
      if (remaining.length > 0) {
        await this.loadConversation(remaining[0].id);
      }
    }
  }

  async sendPrompt(prompt: string): Promise<void> {
    const trimmedPrompt = prompt.trim();
    if (!trimmedPrompt || this.isStreaming()) {
      return;
    }

    this.error.set(null);
    this.status.set('streaming');

    try {
      const conversation = this.activeConversation() ?? await this.createConversation(trimmedPrompt);
      this.status.set('streaming');
      const userTempId = -Date.now();
      const assistantTempId = userTempId - 1;
      const now = new Date().toISOString();

      this.appendMessages(conversation.id, [
        {
          id: userTempId,
          role: 'user',
          content: trimmedPrompt,
          createdAt: now,
          wasSanitized: false,
          detectedTypes: []
        },
        {
          id: assistantTempId,
          role: 'assistant',
          content: '',
          createdAt: now,
          wasSanitized: false,
          detectedTypes: []
        }
      ]);

      this.streamAbortController = new AbortController();
      await this.streamConversation(conversation.id, trimmedPrompt, userTempId, assistantTempId, this.streamAbortController.signal);
      await this.refreshActiveConversation(conversation.id);
      await this.refreshConversationList();
      this.status.set('idle');
    } catch (err: unknown) {
      if (err instanceof DOMException && err.name === 'AbortError') {
        this.status.set('idle');
        return;
      }

      console.error('Failed to stream AI conversation response', err);
      this.error.set('Streaming failed. Make sure the backend and Ollama are running.');
      this.status.set('error');
    } finally {
      this.streamAbortController = null;
    }
  }

  stopStreaming(): void {
    this.streamAbortController?.abort();
  }

  clearError(): void {
    this.error.set(null);
    if (this.status() === 'error') {
      this.status.set('idle');
    }
  }

  private async streamConversation(
    conversationId: number,
    prompt: string,
    userTempId: number,
    assistantTempId: number,
    signal: AbortSignal): Promise<void> {
    const token = await this.auth.getAccessToken();
    const response = await fetch(`${this.apiUrl}/conversations/${conversationId}/stream`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`
      },
      body: JSON.stringify({ prompt }),
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
      const frames = buffer.split('\n\n');
      buffer = frames.pop() ?? '';

      for (const frame of frames) {
        this.handleSseFrame(frame, conversationId, userTempId, assistantTempId);
      }
    }
  }

  private handleSseFrame(frame: string, conversationId: number, userTempId: number, assistantTempId: number): void {
    let eventName = 'message';
    let data = '';

    for (const line of frame.split('\n')) {
      if (line.startsWith('event:')) eventName = line.slice(6).trim();
      else if (line.startsWith('data:')) data += line.slice(5).trim();
    }
    if (!data) return;

    const payload = JSON.parse(data);

    if (eventName === 'started') {
      this.updateMessage(conversationId, userTempId, message => ({
        ...message,
        id: payload.userMessageId,
        content: payload.prompt,
        wasSanitized: payload.wasSanitized,
        detectedTypes: payload.detectedTypes
      }));
    } else if (eventName === 'token') {
      this.updateMessage(conversationId, assistantTempId, message => ({
        ...message,
        content: message.content + payload.text
      }));
    } else if (eventName === 'done' && payload.assistantMessageId) {
      this.updateMessage(conversationId, assistantTempId, message => ({
        ...message,
        id: payload.assistantMessageId,
        createdAt: payload.updatedAt
      }));
    }
  }

  private appendMessages(conversationId: number, messages: AiConversationMessageResponse[]): void {
    this.activeConversation.update(current => current?.id === conversationId
      ? { ...current, messages: [...current.messages, ...messages] }
      : current);
  }

  private updateMessage(
    conversationId: number,
    messageId: number,
    update: (message: AiConversationMessageResponse) => AiConversationMessageResponse): void {
    this.activeConversation.update(current => current?.id === conversationId
      ? {
          ...current,
          messages: current.messages.map(message => message.id === messageId ? update(message) : message)
        }
      : current);
  }

  private async refreshActiveConversation(id: number): Promise<void> {
    this.activeConversation.set(await this.fetchConversation(id));
  }

  private async refreshConversationList(): Promise<void> {
    this.conversations.set(await firstValueFrom(
      this.http.get<AiConversationSummaryResponse[]>(`${this.apiUrl}/conversations`)));
  }

  private fetchConversation(id: number): Promise<AiConversationResponse> {
    return firstValueFrom(this.http.get<AiConversationResponse>(`${this.apiUrl}/conversations/${id}`));
  }

  private toSummary(conversation: AiConversationResponse): AiConversationSummaryResponse {
    const lastMessage = conversation.messages.at(-1);

    return {
      id: conversation.id,
      title: conversation.title,
      createdAt: conversation.createdAt,
      updatedAt: conversation.updatedAt,
      lastMessagePreview: lastMessage?.content,
      messageCount: conversation.messages.length
    };
  }
}