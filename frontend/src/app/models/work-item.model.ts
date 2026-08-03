export enum WorkItemPriority {
  Low = 0,
  Medium = 1,
  High = 2,
  Critical = 3
}

export enum WorkItemStatus {
  Todo = 0,
  InProgress = 1,
  Done = 2
}

export interface WorkItem {
  id: number;
  title: string;
  description?: string;
  priority: WorkItemPriority;
  status: WorkItemStatus;
  createdAt: string;
  dueDate?: string;
}

export interface CreateWorkItemRequest {
  title: string;
  description?: string;
  priority: WorkItemPriority;
  dueDate?: string;
}

export interface UpdateWorkItemRequest {
  title: string;
  description?: string;
  priority: WorkItemPriority;
  status: WorkItemStatus;
  dueDate?: string;
}
