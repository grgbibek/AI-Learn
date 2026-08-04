import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { WorkItemService } from '../../services/work-item.service';
import { AiService } from '../../services/ai.service';
import { WorkItem, WorkItemPriority, WorkItemStatus } from '../../models/work-item.model';

@Component({
  selector: 'app-task-board',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './task-board.component.html',
  styleUrl: './task-board.component.css'
})
export class TaskBoardComponent {
  readonly service = inject(WorkItemService);
  readonly ai = inject(AiService);
  private fb = inject(FormBuilder);

  // Enums for Template Access
  readonly PriorityEnum = WorkItemPriority;
  readonly StatusEnum = WorkItemStatus;

  showForm = signal<boolean>(false);

  // Tracks which task card's AI subtask analysis panel is open
  activeAnalysisItemId = signal<number | null>(null);

  assistantPrompt = signal<string>('');

  // Strongly-typed reactive form
  itemForm = this.fb.group({
    title: ['', [Validators.required, Validators.minLength(3)]],
    description: [''],
    priority: [WorkItemPriority.Medium, [Validators.required]],
    dueDate: ['']
  });

  toggleForm(): void {
    this.showForm.update(val => !val);
  }

  onSubmit(): void {
    if (this.itemForm.invalid) return;

    const val = this.itemForm.value;
    this.service.createWorkItem({
      title: val.title!,
      description: val.description || undefined,
      priority: Number(val.priority),
      dueDate: val.dueDate ? new Date(val.dueDate).toISOString() : undefined
    });

    this.itemForm.reset({ priority: WorkItemPriority.Medium });
    this.showForm.set(false);
  }

  getPriorityLabel(priority: WorkItemPriority): string {
    switch (Number(priority)) {
      case WorkItemPriority.Low: return 'Low';
      case WorkItemPriority.Medium: return 'Medium';
      case WorkItemPriority.High: return 'High';
      case WorkItemPriority.Critical: return 'Critical';
      default: return 'Medium';
    }
  }

  getStatusLabel(status: WorkItemStatus): string {
    switch (Number(status)) {
      case WorkItemStatus.Todo: return 'To Do';
      case WorkItemStatus.InProgress: return 'In Progress';
      case WorkItemStatus.Done: return 'Done';
      default: return 'To Do';
    }
  }

  onFilterStatus(status: WorkItemStatus | 'All'): void {
    this.service.selectedStatusFilter.set(status);
  }

  onSearch(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.service.searchQuery.set(input.value);
  }

  onSuggestSubtasks(item: WorkItem): void {
    this.activeAnalysisItemId.set(item.id);
    this.ai.suggestSubtasks(item.id);
  }

  closeAnalysis(): void {
    this.activeAnalysisItemId.set(null);
    this.ai.clearAnalysis();
  }

  onAssistantPromptChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.assistantPrompt.set(input.value);
  }

  onAskAssistant(): void {
    const prompt = this.assistantPrompt().trim();
    if (!prompt) return;
    this.ai.askWorkloadAssistant(prompt);
  }

  getComplexityClass(level: string): string {
    return 'complexity-' + level.toLowerCase();
  }
}
