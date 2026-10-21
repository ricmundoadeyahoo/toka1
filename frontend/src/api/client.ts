import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';

/**
 * Cliente HTTP único. Todas las llamadas van al API Gateway (mismo origen vía nginx: /api).
 * - Adjunta el access token.
 * - Ante un 401 intenta UNA vez renovar el token (refresh rotation) y reintenta la petición.
 * El store de autenticación se registra con configureAuth() para evitar dependencias circulares.
 */
export const http = axios.create({ baseURL: '/', timeout: 30_000 });

interface AuthHooks {
  getAccessToken: () => string | null;
  refresh: () => Promise<boolean>;
  onSessionExpired: () => void;
}

let hooks: AuthHooks | null = null;
let refreshing: Promise<boolean> | null = null;

export function configureAuth(authHooks: AuthHooks): void {
  hooks = authHooks;
}

http.interceptors.request.use((config) => {
  const token = hooks?.getAccessToken();
  if (token) config.headers.set('Authorization', `Bearer ${token}`);
  return config;
});

type RetriableConfig = InternalAxiosRequestConfig & { _retried?: boolean };

http.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const config = error.config as RetriableConfig | undefined;
    const isAuthCall = config?.url?.includes('/api/auth/');
    if (error.response?.status === 401 && config && !config._retried && !isAuthCall && hooks) {
      config._retried = true;
      refreshing ??= hooks.refresh().finally(() => {
        refreshing = null;
      });
      if (await refreshing) return http(config);
      hooks.onSessionExpired();
    }
    return Promise.reject(error);
  }
);

/** Las consultas al agente de IA pueden tardar (varias llamadas al LLM + herramientas). */
export const AI_TIMEOUT_MS = 300_000;
