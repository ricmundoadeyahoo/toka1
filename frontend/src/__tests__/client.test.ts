import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios';
import { configureAuth, http } from '../api/client';

/** Prueba el interceptor: adjunta token y ante 401 renueva UNA vez y reintenta. */
describe('cliente HTTP', () => {
  let token = 'viejo';
  const refresh = jest.fn<Promise<boolean>, []>();
  const onSessionExpired = jest.fn();
  const calls: string[] = [];

  const adapter: AxiosAdapter = async (config: InternalAxiosRequestConfig) => {
    const auth = String(config.headers.get('Authorization'));
    calls.push(`${config.url} ${auth}`);
    if (auth === 'Bearer viejo') {
      return Promise.reject({ config, response: { status: 401, data: {}, headers: {}, config, statusText: '' } });
    }
    return { data: { ok: true }, status: 200, statusText: 'OK', headers: {}, config };
  };

  beforeEach(() => {
    token = 'viejo';
    calls.length = 0;
    refresh.mockReset();
    onSessionExpired.mockReset();
    http.defaults.adapter = adapter;
    configureAuth({ getAccessToken: () => token, refresh, onSessionExpired });
  });

  it('renueva el token y reintenta la petición original', async () => {
    refresh.mockImplementation(async () => {
      token = 'nuevo';
      return true;
    });
    const response = await http.get('/api/users');
    expect(response.data).toEqual({ ok: true });
    expect(calls).toEqual(['/api/users Bearer viejo', '/api/users Bearer nuevo']);
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('si la renovación falla, cierra la sesión y propaga el 401', async () => {
    refresh.mockResolvedValue(false);
    await expect(http.get('/api/users')).rejects.toMatchObject({ response: { status: 401 } });
    expect(onSessionExpired).toHaveBeenCalled();
  });

  it('no intenta renovar en endpoints de autenticación', async () => {
    await expect(http.post('/api/auth/login', {})).rejects.toBeDefined();
    expect(refresh).not.toHaveBeenCalled();
  });
});
