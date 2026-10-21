import { create } from 'zustand';
import { rolesApi } from '../api/endpoints';
import { toApiError } from '../api/errors';
import type { ApiError, Role, UserRoles } from '../types';
import { toast } from './toastStore';

interface RolesState {
  roles: Role[];
  selected: UserRoles | null;
  loading: boolean;
  error: ApiError | null;
  fetchRoles: () => Promise<void>;
  loadUserRoles: (userId: string) => Promise<void>;
  assign: (userId: string, roleName: string) => Promise<boolean>;
  revoke: (userId: string, roleName: string) => Promise<boolean>;
}

export const useRolesStore = create<RolesState>()((set) => ({
  roles: [],
  selected: null,
  loading: false,
  error: null,

  fetchRoles: async () => {
    set({ loading: true, error: null });
    try {
      set({ roles: await rolesApi.list(), loading: false });
    } catch (e) {
      set({ loading: false, error: toApiError(e) });
    }
  },

  loadUserRoles: async (userId) => {
    set({ loading: true, error: null, selected: null });
    try {
      set({ selected: await rolesApi.userRoles(userId), loading: false });
    } catch (e) {
      set({ loading: false, error: toApiError(e) });
    }
  },

  assign: async (userId, roleName) => {
    try {
      set({ selected: await rolesApi.assign(userId, roleName) });
      toast.success(`Rol ${roleName} asignado.`);
      return true;
    } catch (e) {
      toast.error(toApiError(e).message);
      return false;
    }
  },

  revoke: async (userId, roleName) => {
    try {
      set({ selected: await rolesApi.revoke(userId, roleName) });
      toast.success(`Rol ${roleName} revocado.`);
      return true;
    } catch (e) {
      toast.error(toApiError(e).message);
      return false;
    }
  }
}));
