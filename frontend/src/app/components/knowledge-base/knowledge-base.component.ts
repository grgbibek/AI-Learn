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
  selectedMarkdownFiles = signal<File[]>([]);
  folderPath = signal<string>('');
  folderProjectName = signal<string>('');
  question = signal<string>('');
  mode = signal<KnowledgeBaseMode>('sqlHybrid');

  setMode(mode: KnowledgeBaseMode): void {
    this.mode.set(mode);
    this.ai.ingestResult.set(null);
    this.ai.ingestFilesResult.set(null);
    this.ai.ingestFolderResult.set(null);
    this.ai.askResult.set(null);
    this.ai.ingestError.set(null);
    this.ai.ingestFolderError.set(null);
    this.ai.askError.set(null);
  }

  onMarkdownFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    this.selectedMarkdownFiles.set(files);
    this.ai.ingestFilesResult.set(null);
    this.ai.ingestError.set(null);
  }

  onIngestFiles(): void {
    const files = this.selectedMarkdownFiles();
    if (files.length === 0) {
      return;
    }

    this.ai.ingestMarkdownFiles(files, this.mode());
  }

  onIngestFolder(): void {
    const folderPath = this.folderPath().trim();
    if (!folderPath) {
      return;
    }

    this.ai.ingestFolder(folderPath, this.folderProjectName().trim(), this.mode());
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
