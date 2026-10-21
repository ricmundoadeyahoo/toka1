import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';
import { configureAuth } from '../api/client';
import { authApi, type RegisterPayload } from '../api/endpoints';
import { toApiError } from '../api/errors';
import type { ApiError, AuthenticatedUser, TokenResponse } from '../types';

interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  user: AuthenticatedUser | null;
  loading: boolean;
  error: ApiError | null;
  login: (email: string, password: string) => Promise<boolean>;
  register: (payload: RegisterPayload) => Promise<boolean>;
  refresh: () => Promise<boolean>;
  logout: () => Promise<void>;
  clearSession: () => void;
  clearError: () => void;
  hasPermission: (permission: string) => boolean;
}

const emptySession = { accessToken: null, refreshToken: null, user: null };

function fromTokens(tokens: TokenResponse) {
  return { accessToken: tokens.accessToken, refreshToken: tokens.refreshToken, user: tokens.user };
}

/**
 * Estado de sesión. Se persiste en sessionStorage (se borra al cerrar la pestaña).
 * Ver Sugerencias: en producción, preferir cookies httpOnly emitidas por un BFF.
 */
export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      ...emptySession,
      loading: false,
      error: null,

      login: async (email, password) => {
        set({ loading: true, error: null });
        try {
          const tokens = await authApi.login(email.trim(), password);
          set({ ...fromTokens(tokens), loading: false });
          return true;
        } catch (e) {
          set({ loading: false, error: toApiError(e) });
          return false;
        }
      },

      register: async (payload) => {
        set({ loading: true, error: null });
        try {
          await authApi.register(payload);
          set({ loading: false });
          return true;
        } catch (e) {
          set({ loading: false, error: toApiError(e) });
          return false;
        }
      },

      refresh: async () => {
        const token = get().refreshToken;
        if (!token) return false;
        try {
          set(fromTokens(await authApi.refresh(token)));
          return true;
        } catch {
          set(emptySession);
          return false;
        }
      },

      logout: async () => {
        try {
          if (get().accessToken) await authApi.logout(get().refreshToken);
        } catch {
          // el cierre local de sesión ocurre aunque el servidor no responda
        } finally {
          set({ ...emptySession, error: null });
        }
      },

      clearSession: () => set(emptySession),
      clearError: () => set({ error: null }),
      hasPermission: (permission) => get().user?.permissions.includes(permission) ?? false
    }),
    {
      name: 'toka-session',
      storage: createJSONStorage(() => sessionStorage),
      partialize: (s) => ({ accessToken: s.accessToken, refreshToken: s.refreshToken, user: s.user })
    }
  )
);

configureAuth({
  getAccessToken: () => useAuthStore.getState().accessToken,
  refresh: () => useAuthStore.getState().refresh(),
  onSessionExpired: () => useAuthStore.getState().clearSession()
});
