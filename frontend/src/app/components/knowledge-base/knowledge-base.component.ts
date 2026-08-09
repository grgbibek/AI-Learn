import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AiService, KnowledgeBaseMode } from '../../services/ai.service';

import { MarkdownRenderComponent } from '../shared/markdown-render.component';

@Component({
  selector: 'app-knowledge-base',
  standalone: true,
  imports: [FormsModule, MarkdownRenderComponent],
  templateUrl: './knowledge-base.component.html',
  styleUrl: './knowledge-base.component.css'
})
export class KnowledgeBaseComponent {
  readonly ai = inject(AiService);

  docTitle = signal<string>('');
  docContent = signal<string>('');
  question = signal<string>('');
  mode = signal<KnowledgeBaseMode>('sqlHybrid');

  setMode(mode: KnowledgeBaseMode): void {
    this.mode.set(mode);
    this.ai.ingestResult.set(null);
    this.ai.askResult.set(null);
    this.ai.ingestError.set(null);
    this.ai.askError.set(null);
  }

  onIngest(): void {
    const title = this.docTitle().trim();
    const content = this.docContent().trim();
    if (!title || !content) {
      return;
    }
    this.ai.ingestDocument(title, content, this.mode());
  }

  onAsk(): void {
    const question = this.question().trim();
    if (!question) {
      return;
    }
    this.ai.askKnowledgeBase(question, 3, this.mode());
  }

  onStopAsk(): void {
    this.ai.stopAsk();
  }
}
