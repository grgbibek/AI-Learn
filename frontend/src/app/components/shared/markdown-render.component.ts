import { Component, Input, OnChanges, SimpleChanges, inject, signal } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { marked } from 'marked';
import hljs from 'highlight.js';

@Component({
  selector: 'app-markdown-render',
  standalone: true,
  template: `
    <div class="markdown-body" [innerHTML]="safeHtml()"></div>
  `,
  styles: [`
    :host {
      display: block;
    }
    .markdown-body {
      font-size: 0.95rem;
      line-height: 1.6;
      color: #e2e8f0;
    }
    .markdown-body p {
      margin-bottom: 0.75rem;
    }
    .markdown-body p:last-child {
      margin-bottom: 0;
    }
    .markdown-body h1, .markdown-body h2, .markdown-body h3, .markdown-body h4 {
      color: #f8fafc;
      font-weight: 600;
      margin-top: 1rem;
      margin-bottom: 0.5rem;
    }
    .markdown-body ul, .markdown-body ol {
      padding-left: 1.25rem;
      margin-bottom: 0.75rem;
    }
    .markdown-body li {
      margin-bottom: 0.25rem;
    }
    .markdown-body code {
      font-family: 'Fira Code', Consolas, Monaco, monospace;
      background: #1e293b;
      color: #38bdf8;
      padding: 0.15rem 0.35rem;
      border-radius: 4px;
      font-size: 0.85em;
    }
    .markdown-body pre {
      background: #090d16;
      padding: 1rem;
      border-radius: 8px;
      overflow-x: auto;
      border: 1px solid #1e293b;
      margin-top: 0.75rem;
      margin-bottom: 0.75rem;
    }
    .markdown-body pre code {
      background: transparent;
      padding: 0;
      color: #f8fafc;
    }
    .markdown-body blockquote {
      border-left: 4px solid #3b82f6;
      padding-left: 0.75rem;
      margin: 0.75rem 0;
      color: #94a3b8;
      font-style: italic;
    }
    .markdown-body table {
      width: 100%;
      border-collapse: collapse;
      margin: 0.75rem 0;
    }
    .markdown-body th, .markdown-body td {
      border: 1px solid #334155;
      padding: 0.5rem 0.75rem;
      text-align: left;
    }
    .markdown-body th {
      background: #1e293b;
      color: #f8fafc;
    }
  `]
})
export class MarkdownRenderComponent implements OnChanges {
  @Input() content: string = '';

  private sanitizer = inject(DomSanitizer);
  readonly safeHtml = signal<SafeHtml>('');

  constructor() {
    // Configure marked renderer for code highlight
    marked.setOptions({
      gfm: true,
      breaks: true
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['content']) {
      this.renderMarkdown(this.content || '');
    }
  }

  private renderMarkdown(rawText: string): void {
    if (!rawText) {
      this.safeHtml.set('');
      return;
    }

    try {
      const parsedHtml = marked.parse(rawText) as string;
      // Highlight code blocks
      const tempDiv = document.createElement('div');
      tempDiv.innerHTML = parsedHtml;
      
      const codeBlocks = tempDiv.querySelectorAll('pre code');
      codeBlocks.forEach((block) => {
        hljs.highlightElement(block as HTMLElement);
      });

      this.safeHtml.set(this.sanitizer.bypassSecurityTrustHtml(tempDiv.innerHTML));
    } catch (e) {
      console.error('Error parsing markdown:', e);
      this.safeHtml.set(this.sanitizer.bypassSecurityTrustHtml(rawText));
    }
  }
}
