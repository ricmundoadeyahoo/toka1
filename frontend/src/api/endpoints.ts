import { AI_TIMEOUT_MS, http } from './client';
import type {
  AskResponse,
  AuditEntry,
  MetricsSummary,
  PagedResult,
  Role,
  TokenResponse,
  User,
  UserRoles
} from '../types';

export interface RegisterPayload {
  email: string;
  password: string;
  fullName: string;
  department?: string;
}

export const authApi = {
  login: (email: string, password: string) =>
    http.post<TokenResponse>('/api/auth/login', { email, password }).then((r) => r.data),
  register: (payload: RegisterPayload) =>
    http.post<{ userId: string; email: string }>('/api/auth/register', payload).then((r) => r.data),
  refresh: (refreshToken: string) =>
    http.post<TokenResponse>('/api/auth/refresh', { refreshToken }).then((r) => r.data),
  logout: (refreshToken: string | null) => http.post('/api/auth/logout', { refreshToken }).then(() => undefined)
};

export interface UserQuery {
  search?: string;
  status?: string;
  page?: number;
  pageSize?: number;
}

export const usersApi = {
  list: (query: UserQuery) => http.get<PagedResult<User>>('/api/users', { params: query }).then((r) => r.data),
  me: () => http.get<User>('/api/users/me').then((r) => r.data),
  update: (id: string, data: { fullName: string; department?: string | null; jobTitle?: string | null }) =>
    http.put<User>(`/api/users/${id}`, data).then((r) => r.data),
  deactivate: (id: string) => http.post<User>(`/api/users/${id}/deactivate`).then((r) => r.data),
  activate: (id: string) => http.post<User>(`/api/users/${id}/activate`).then((r) => r.data)
};

export const rolesApi = {
  list: () => http.get<Role[]>('/api/roles').then((r) => r.data),
  userRoles: (userId: string) => http.get<UserRoles>(`/api/roles/users/${userId}`).then((r) => r.data),
  assign: (userId: string, roleName: string) =>
    http.post<UserRoles>(`/api/roles/users/${userId}/assign`, { roleName }).then((r) => r.data),
  revoke: (userId: string, roleName: string) =>
    http.delete<UserRoles>(`/api/roles/users/${userId}/${encodeURIComponent(roleName)}`).then((r) => r.data),
  create: (name: string, description: string, permissions: string[]) =>
    http.post<Role>('/api/roles', { name, description, permissions }).then((r) => r.data)
};

export interface AuditQuery {
  eventType?: string;
  category?: string;
  success?: boolean;
  page?: number;
  pageSize?: number;
}

export const auditApi = {
  search: (query: AuditQuery) => http.get<PagedResult<AuditEntry>>('/api/audit', { params: query }).then((r) => r.data)
};

export const aiApi = {
  ask: (question: string, useCache = true) =>
    http.post<AskResponse>('/api/ai/ask', { question, useCache }, { timeout: AI_TIMEOUT_MS }).then((r) => r.data),
  status: () =>
    http
      .get<{ chunks: number; chatModel: string; embeddingModel: string; promptVersion: string }>('/api/ai/knowledge/status')
      .then((r) => r.data),
  metrics: () => http.get<MetricsSummary>('/api/ai/metrics/summary').then((r) => r.data)
};
