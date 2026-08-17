import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { StreamingAssistantService } from '../../services/streaming-assistant.service';
import { MarkdownRenderComponent } from '../shared/markdown-render.component';

@Component({
  selector: 'app-streaming-assistant',
  standalone: true,
  imports: [FormsModule, MarkdownRenderComponent],
  templateUrl: './streaming-assistant.component.html',
  styleUrl: './streaming-assistant.component.css'
})
export class StreamingAssistantComponent implements OnInit {
  readonly assistant = inject(StreamingAssistantService);
  readonly prompt = signal<string>('Explain how Angular Signals should consume a .NET SSE stream in this app.');

  ngOnInit(): void {
    void this.assistant.loadConversations();
  }

  onNewConversation(): void {
    void this.assistant.createConversation();
  }

  onSelectConversation(id: number): void {
    void this.assistant.loadConversation(id);
  }

  onDeleteConversation(event: MouseEvent, id: number): void {
    event.stopPropagation();
    void this.assistant.deleteConversation(id);
  }

  onAsk(): void {
    const prompt = this.prompt().trim();
    if (!prompt || this.assistant.isStreaming()) {
      return;
    }

    void this.assistant.sendPrompt(prompt).then(() => this.prompt.set(''));
  }

  onStop(): void {
    this.assistant.stopStreaming();
  }

  onDismissError(): void {
    this.assistant.clearError();
  }
}