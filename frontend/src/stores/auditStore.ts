import { create } from 'zustand';
import { auditApi, type AuditQuery } from '../api/endpoints';
import { toApiError } from '../api/errors';
import type { ApiError, AuditEntry } from '../types';

interface AuditState {
  items: AuditEntry[];
  totalCount: number;
  totalPages: number;
  query: AuditQuery & { page: number; pageSize: number };
  loading: boolean;
  error: ApiError | null;
  fetch: (patch?: Partial<AuditState['query']>) => Promise<void>;
}

export const useAuditStore = create<AuditState>()((set, get) => ({
  items: [],
  totalCount: 0,
  totalPages: 0,
  query: { page: 1, pageSize: 20 },
  loading: false,
  error: null,

  fetch: async (patch) => {
    const query = { ...get().query, ...patch };
    set({ query, loading: true, error: null });
    try {
      const result = await auditApi.search(query);
      set({ items: result.items, totalCount: result.totalCount, totalPages: result.totalPages, loading: false });
    } catch (e) {
      set({ loading: false, error: toApiError(e) });
    }
  }
}));
