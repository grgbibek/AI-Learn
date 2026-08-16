import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AiService } from '../../services/ai.service';
import { MarkdownRenderComponent } from '../shared/markdown-render.component';

@Component({
  selector: 'app-streaming-assistant',
  standalone: true,
  imports: [FormsModule, MarkdownRenderComponent],
  templateUrl: './streaming-assistant.component.html',
  styleUrl: './streaming-assistant.component.css'
})
export class StreamingAssistantComponent {
  readonly ai = inject(AiService);
  readonly prompt = signal<string>('Explain how Angular Signals should consume a .NET SSE stream in this app.');

  onAsk(): void {
    const prompt = this.prompt().trim();
    if (!prompt || this.ai.streamingLoading()) {
      return;
    }

    this.ai.askStreamingAssistant(prompt);
  }

  onStop(): void {
    this.ai.stopStreamingAssistant();
  }

  onClear(): void {
    this.ai.clearStreamingAssistant();
  }
}