import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AiService } from '../../services/ai.service';

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

  onIngest(): void {
    const title = this.docTitle().trim();
    const content = this.docContent().trim();
    if (!title || !content) {
      return;
    }
    this.ai.ingestDocument(title, content);
  }

  onAsk(): void {
    const question = this.question().trim();
    if (!question) {
      return;
    }
    this.ai.askKnowledgeBase(question);
  }

  onStopAsk(): void {
    this.ai.stopAsk();
  }
}
