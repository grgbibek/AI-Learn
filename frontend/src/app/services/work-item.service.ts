import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { WorkItem, CreateWorkItemRequest, UpdateWorkItemRequest, WorkItemStatus } from '../models/work-item.model';

@Injectable({
  providedIn: 'root'
})
export class WorkItemService {
  private http = inject(HttpClient);
  private apiUrl = 'http://localhost:5198/api/workitems';

  // Writable signals for state
  private itemsSignal = signal<WorkItem[]>([]);
  readonly loading = signal<boolean>(false);
  readonly error = signal<string | null>(null);

  // Filter signals
  readonly selectedStatusFilter = signal<WorkItemStatus | 'All'>('All');
  readonly searchQuery = signal<string>('');

  // Computed signals
  readonly items = computed(() => {
    let list = this.itemsSignal();
    const status = this.selectedStatusFilter();
    const query = this.searchQuery().toLowerCase().trim();

    if (status !== 'All') {
      list = list.filter(item => item.status === status);
    }

    if (query) {
      list = list.filter(item =>
        item.title.toLowerCase().includes(query) ||
        (item.description && item.description.toLowerCase().includes(query))
      );
    }

    return list;
  });

  readonly stats = computed(() => {
    const all = this.itemsSignal();
    return {
      total: all.length,
      todo: all.filter(i => i.status === WorkItemStatus.Todo).length,
      inProgress: all.filter(i => i.status === WorkItemStatus.InProgress).length,
      done: all.filter(i => i.status === WorkItemStatus.Done).length
    };
  });

  constructor() {
    this.loadWorkItems();
  }

  loadWorkItems(): void {
    this.loading.set(true);
    this.error.set(null);

    this.http.get<WorkItem[]>(this.apiUrl).subscribe({
      next: (items: WorkItem[]) => {
        this.itemsSignal.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        console.error('Failed to fetch work items', err);
        this.error.set('Could not connect to .NET API backend.');
        this.loading.set(false);
      }
    });
  }

  createWorkItem(req: CreateWorkItemRequest): void {
    this.loading.set(true);
    this.http.post<WorkItem>(this.apiUrl, req).subscribe({
      next: (newItem: WorkItem) => {
        this.itemsSignal.update((items: WorkItem[]) => [newItem, ...items]);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        console.error('Failed to create work item', err);
        this.error.set('Failed to create work item.');
        this.loading.set(false);
      }
    });
  }

  updateWorkItem(id: number, req: UpdateWorkItemRequest): void {
    this.loading.set(true);
    this.http.put<WorkItem>(`${this.apiUrl}/${id}`, req).subscribe({
      next: (updatedItem: WorkItem) => {
        this.itemsSignal.update((items: WorkItem[]) =>
          items.map(item => item.id === id ? updatedItem : item)
        );
        this.loading.set(false);
      },
      error: (err: unknown) => {
        console.error('Failed to update work item', err);
        this.error.set('Failed to update work item.');
        this.loading.set(false);
      }
    });
  }

  updateStatus(id: number, currentItem: WorkItem, newStatus: WorkItemStatus): void {
    const updateReq: UpdateWorkItemRequest = {
      title: currentItem.title,
      description: currentItem.description ?? undefined,
      priority: currentItem.priority,
      status: newStatus,
      dueDate: currentItem.dueDate ?? undefined
    };
    this.updateWorkItem(id, updateReq);
  }

  deleteWorkItem(id: number): void {
    this.loading.set(true);
    this.http.delete<void>(`${this.apiUrl}/${id}`).subscribe({
      next: () => {
        this.itemsSignal.update((items: WorkItem[]) => items.filter(i => i.id !== id));
        this.loading.set(false);
      },
      error: (err: unknown) => {
        console.error('Failed to delete work item', err);
        this.error.set('Failed to delete work item.');
        this.loading.set(false);
      }
    });
  }
}
