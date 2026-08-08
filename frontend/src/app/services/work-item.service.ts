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
  readonly optimisticMessage = signal<string | null>(null);

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

  // Optimistic Creation with Rollback
  createWorkItem(req: CreateWorkItemRequest): void {
    const tempId = -Date.now();
    const optimisticItem: WorkItem = {
      id: tempId,
      title: req.title,
      description: req.description,
      priority: req.priority,
      status: WorkItemStatus.Todo,
      createdAt: new Date().toISOString(),
      dueDate: req.dueDate
    };

    const previousSnapshot = this.itemsSignal();
    // Optimistic Update
    this.itemsSignal.update(items => [optimisticItem, ...items]);
    this.showOptimisticToast('⚡ Task added optimistically');

    this.http.post<WorkItem>(this.apiUrl, req).subscribe({
      next: (realItem: WorkItem) => {
        // Swap temp item with server response
        this.itemsSignal.update(items => items.map(i => i.id === tempId ? realItem : i));
      },
      error: (err: unknown) => {
        console.error('Failed to create work item, rolling back state', err);
        // Rollback state
        this.itemsSignal.set(previousSnapshot);
        this.error.set('Failed to save task to backend. State rolled back.');
      }
    });
  }

  // Optimistic Status Update with Rollback
  updateStatus(id: number, currentItem: WorkItem, newStatus: WorkItemStatus): void {
    if (currentItem.status === newStatus) return;

    const previousSnapshot = this.itemsSignal();

    // Optimistic Update immediately in UI
    this.itemsSignal.update(items => items.map(item =>
      item.id === id ? { ...item, status: newStatus } : item
    ));

    this.showOptimisticToast(`⚡ Moved to ${newStatus}`);

    const updateReq: UpdateWorkItemRequest = {
      title: currentItem.title,
      description: currentItem.description ?? undefined,
      priority: currentItem.priority,
      status: newStatus,
      dueDate: currentItem.dueDate ?? undefined
    };

    this.http.put<WorkItem>(`${this.apiUrl}/${id}`, updateReq).subscribe({
      next: (serverUpdatedItem: WorkItem) => {
        this.itemsSignal.update(items => items.map(item =>
          item.id === id ? serverUpdatedItem : item
        ));
      },
      error: (err: unknown) => {
        console.error('Status update failed on backend, rolling back state', err);
        // Rollback state
        this.itemsSignal.set(previousSnapshot);
        this.error.set(`Failed to update status for "${currentItem.title}". Rolled back.`);
      }
    });
  }

  // Optimistic Deletion with Rollback
  deleteWorkItem(id: number): void {
    const previousSnapshot = this.itemsSignal();
    const itemToDelete = previousSnapshot.find(i => i.id === id);

    // Optimistic deletion
    this.itemsSignal.update(items => items.filter(i => i.id !== id));
    this.showOptimisticToast('⚡ Task removed optimistically');

    this.http.delete<void>(`${this.apiUrl}/${id}`).subscribe({
      next: () => {
        // Confirmed deleted on backend
      },
      error: (err: unknown) => {
        console.error('Failed to delete work item, rolling back state', err);
        this.itemsSignal.set(previousSnapshot);
        this.error.set(`Failed to delete "${itemToDelete?.title || 'item'}". State restored.`);
      }
    });
  }

  private showOptimisticToast(msg: string): void {
    this.optimisticMessage.set(msg);
    setTimeout(() => {
      if (this.optimisticMessage() === msg) {
        this.optimisticMessage.set(null);
      }
    }, 2500);
  }
}
