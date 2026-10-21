export interface AuthenticatedUser {
  id: string;
  email: string;
  roles: string[];
  permissions: string[];
}

export interface TokenResponse {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
  refreshToken: string | null;
  user: AuthenticatedUser | null;
}

export type UserStatus = 'Active' | 'Deactivated';

export interface User {
  id: string;
  email: string;
  fullName: string;
  department: string | null;
  jobTitle: string | null;
  status: UserStatus;
  createdAt: string;
  updatedAt: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface Role {
  id: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  permissions: string[];
}

export interface UserRoles {
  userId: string;
  roles: string[];
  permissions: string[];
}

export interface AuditEntry {
  id: string;
  eventType: string;
  category: string;
  occurredAt: string;
  source: string;
  actorId: string | null;
  actorEmail: string | null;
  subjectId: string | null;
  ipAddress: string | null;
  correlationId: string | null;
  success: boolean;
  details: Record<string, string>;
}

export interface QualityCheck {
  name: string;
  passed: boolean;
  detail: string;
  weight: number;
}

export interface AnswerMetrics {
  latencyMs: number;
  retrievalMs: number;
  llmMs: number;
  toolMs: number;
  llmCalls: number;
  promptTokens: number;
  cachedPromptTokens: number;
  completionTokens: number;
  embeddingTokens: number;
  totalTokens: number;
  estimatedCostUsd: number;
  referenceCostUsd: number;
  referenceModel: string;
  model: string;
  promptVersion: string;
  cacheHit: boolean;
}

export interface AskResponse {
  queryId: string;
  answer: string;
  sources: { index: number; title: string; source: string; score: number }[];
  toolsUsed: string[];
  metrics: AnswerMetrics;
  quality: { checks: QualityCheck[]; score: number; passed: boolean };
  possibleInjection: boolean;
}

export interface MetricsSummary {
  queries: number;
  successful: number;
  cacheHitRate: number;
  avgLatencyMs: number;
  p50LatencyMs: number;
  p95LatencyMs: number;
  totalPromptTokens: number;
  totalCompletionTokens: number;
  totalEmbeddingTokens: number;
  avgTokensPerQuery: number;
  totalEstimatedCostUsd: number;
  totalReferenceCostUsd: number;
  avgQualityScore: number;
  qualityPassRate: number;
  toolUsage: Record<string, number>;
}

/** Error normalizado de la API (ProblemDetails / OAuth). */
export interface ApiError {
  status: number;
  code: string;
  message: string;
  correlationId?: string;
}

export const Permissions = {
  UsersRead: 'users:read',
  UsersWrite: 'users:write',
  RolesRead: 'roles:read',
  RolesManage: 'roles:manage',
  AuditRead: 'audit:read',
  AiUse: 'ai:use',
  AiAdmin: 'ai:admin'
} as const;
