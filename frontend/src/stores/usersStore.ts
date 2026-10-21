import { create } from 'zustand';
import { usersApi, type UserQuery } from '../api/endpoints';
import { toApiError } from '../api/errors';
import type { ApiError, User } from '../types';
import { toast } from './toastStore';

interface UsersState {
  items: User[];
  totalCount: number;
  totalPages: number;
  query: Required<Pick<UserQuery, 'page' | 'pageSize'>> & Pick<UserQuery, 'search' | 'status'>;
  loading: boolean;
  saving: boolean;
  error: ApiError | null;
  fetch: (patch?: Partial<UsersState['query']>) => Promise<void>;
  update: (id: string, data: { fullName: string; department?: string | null; jobTitle?: string | null }) => Promise<boolean>;
  toggleStatus: (user: User) => Promise<boolean>;
}

export const useUsersStore = create<UsersState>()((set, get) => {
  const replace = (user: User) => set({ items: get().items.map((u) => (u.id === user.id ? user : u)) });

  return {
    items: [],
    totalCount: 0,
    totalPages: 0,
    query: { page: 1, pageSize: 10 },
    loading: false,
    saving: false,
    error: null,

    fetch: async (patch) => {
      const query = { ...get().query, ...patch };
      set({ query, loading: true, error: null });
      try {
        const result = await usersApi.list(query);
        set({ items: result.items, totalCount: result.totalCount, totalPages: result.totalPages, loading: false });
      } catch (e) {
        set({ loading: false, error: toApiError(e) });
      }
    },

    update: async (id, data) => {
      set({ saving: true });
      try {
        replace(await usersApi.update(id, data));
        toast.success('Usuario actualizado.');
        return true;
      } catch (e) {
        toast.error(toApiError(e).message);
        return false;
      } finally {
        set({ saving: false });
      }
    },

    toggleStatus: async (user) => {
      set({ saving: true });
      try {
        const updated = user.status === 'Active' ? await usersApi.deactivate(user.id) : await usersApi.activate(user.id);
        replace(updated);
        toast.success(updated.status === 'Active' ? 'Usuario reactivado.' : 'Usuario desactivado.');
        return true;
      } catch (e) {
        toast.error(toApiError(e).message);
        return false;
      } finally {
        set({ saving: false });
      }
    }
  };
});
