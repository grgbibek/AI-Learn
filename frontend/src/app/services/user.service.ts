import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

export interface AppUser {
  id: number;
  userName: string;
  email: string;
  role: 'User' | 'Admin';
  dailyAiRequestLimit: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string | null;
}

export interface CreateUserRequest {
  userName: string;
  email: string;
  password: string;
  role: 'User' | 'Admin';
  dailyAiRequestLimit: number;
  isActive: boolean;
}

export interface UpdateUserRequest {
  email: string;
  password?: string | null;
  role: 'User' | 'Admin';
  dailyAiRequestLimit: number;
  isActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class UserService {
  private http = inject(HttpClient);
  private apiUrl = 'http://localhost:5198/api/users';

  getUsers() {
    return this.http.get<AppUser[]>(`${this.apiUrl}/`);
  }

  createUser(request: CreateUserRequest) {
    return this.http.post<AppUser>(`${this.apiUrl}/`, request);
  }

  updateUser(id: number, request: UpdateUserRequest) {
    return this.http.put<AppUser>(`${this.apiUrl}/${id}`, request);
  }
}