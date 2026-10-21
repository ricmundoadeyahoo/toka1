import { aiApi, authApi, usersApi } from '../api/endpoints';
import { useAiStore } from '../stores/aiStore';
import { useAuthStore } from '../stores/authStore';
import { useToastStore } from '../stores/toastStore';
import { useUsersStore } from '../stores/usersStore';
import type { AskResponse, User } from '../types';

jest.mock('../api/endpoints');

const mockedAuth = authApi as jest.Mocked<typeof authApi>;
const mockedUsers = usersApi as jest.Mocked<typeof usersApi>;
const mockedAi = aiApi as jest.Mocked<typeof aiApi>;

const tokens = {
  accessToken: 'at',
  tokenType: 'Bearer',
  expiresIn: 900,
  refreshToken: 'rt',
  user: { id: 'u1', email: 'ana@toka.com', roles: ['Admin'], permissions: ['users:read', 'users:write'] }
};

const user: User = {
  id: 'u1', email: 'ana@toka.com', fullName: 'Ana', department: null, jobTitle: null,
  status: 'Active', createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z'
};

beforeEach(() => {
  jest.resetAllMocks();
  useAuthStore.setState({ accessToken: null, refreshToken: null, user: null, loading: false, error: null });
  useToastStore.setState({ toasts: [] });
  useAiStore.setState({ messages: [], asking: false, summary: null, summaryError: null });
  sessionStorage.clear();
});

describe('authStore', () => {
  it('login exitoso guarda sesión y permisos', async () => {
    mockedAuth.login.mockResolvedValue(tokens);
    await expect(useAuthStore.getState().login(' ana@toka.com ', 'x')).resolves.toBe(true);
    expect(mockedAuth.login).toHaveBeenCalledWith('ana@toka.com', 'x');
    expect(useAuthStore.getState().accessToken).toBe('at');
    expect(useAuthStore.getState().hasPermission('users:write')).toBe(true);
    expect(useAuthStore.getState().hasPermission('audit:read')).toBe(false);
    expect(sessionStorage.getItem('toka-session')).toContain('"accessToken":"at"');
  });

  it('login fallido expone error normalizado', async () => {
    mockedAuth.login.mockRejectedValue({ response: { status: 401, data: { detail: 'Email o contraseña incorrectos.' } } });
    await expect(useAuthStore.getState().login('a@b.com', 'x')).resolves.toBe(false);
    expect(useAuthStore.getState().error?.message).toBe('Email o contraseña incorrectos.');
    expect(useAuthStore.getState().loading).toBe(false);
    useAuthStore.getState().clearError();
    expect(useAuthStore.getState().error).toBeNull();
  });

  it('refresh rota tokens y limpia sesión si falla', async () => {
    useAuthStore.setState({ refreshToken: 'rt-old' });
    mockedAuth.refresh.mockResolvedValueOnce({ ...tokens, accessToken: 'at2', refreshToken: 'rt2' });
    await expect(useAuthStore.getState().refresh()).resolves.toBe(true);
    expect(useAuthStore.getState().refreshToken).toBe('rt2');

    mockedAuth.refresh.mockRejectedValueOnce(new Error('x'));
    await expect(useAuthStore.getState().refresh()).resolves.toBe(false);
    expect(useAuthStore.getState().accessToken).toBeNull();

    await expect(useAuthStore.getState().refresh()).resolves.toBe(false); // sin refresh token
  });

  it('logout limpia la sesión aunque el servidor falle', async () => {
    useAuthStore.setState({ accessToken: 'at', refreshToken: 'rt', user: tokens.user });
    mockedAuth.logout.mockRejectedValue(new Error('down'));
    await useAuthStore.getState().logout();
    expect(mockedAuth.logout).toHaveBeenCalledWith('rt');
    expect(useAuthStore.getState().user).toBeNull();
  });

  it('register reporta éxito y error', async () => {
    mockedAuth.register.mockResolvedValueOnce({ userId: 'u', email: 'e' });
    await expect(useAuthStore.getState().register({ email: 'e', password: 'p', fullName: 'n' })).resolves.toBe(true);
    mockedAuth.register.mockRejectedValueOnce({ response: { status: 409, data: { detail: 'Ya existe' } } });
    await expect(useAuthStore.getState().register({ email: 'e', password: 'p', fullName: 'n' })).resolves.toBe(false);
    expect(useAuthStore.getState().error?.status).toBe(409);
  });
});

describe('usersStore', () => {
  it('fetch maneja carga, datos y error', async () => {
    mockedUsers.list.mockResolvedValueOnce({ items: [user], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 });
    const pending = useUsersStore.getState().fetch({ search: 'ana' });
    expect(useUsersStore.getState().loading).toBe(true);
    await pending;
    expect(useUsersStore.getState().items).toHaveLength(1);
    expect(mockedUsers.list).toHaveBeenCalledWith(expect.objectContaining({ search: 'ana', page: 1 }));

    mockedUsers.list.mockRejectedValueOnce({ response: { status: 403 } });
    await useUsersStore.getState().fetch();
    expect(useUsersStore.getState().error?.status).toBe(403);
  });

  it('toggleStatus actualiza la fila y notifica', async () => {
    useUsersStore.setState({ items: [user] });
    mockedUsers.deactivate.mockResolvedValue({ ...user, status: 'Deactivated' });
    await expect(useUsersStore.getState().toggleStatus(user)).resolves.toBe(true);
    expect(useUsersStore.getState().items[0].status).toBe('Deactivated');
    expect(useToastStore.getState().toasts[0].message).toMatch(/desactivado/);
  });

  it('update notifica error', async () => {
    mockedUsers.update.mockRejectedValue({ response: { status: 400, data: { detail: 'Nombre inválido' } } });
    await expect(useUsersStore.getState().update('u1', { fullName: 'x' })).resolves.toBe(false);
    expect(useToastStore.getState().toasts[0]).toMatchObject({ kind: 'error', message: 'Nombre inválido' });
  });
});

describe('aiStore', () => {
  it('agrega pregunta y respuesta; en error muestra mensaje', async () => {
    mockedAi.ask.mockResolvedValueOnce({ answer: 'Respuesta [1]' } as AskResponse);
    await useAiStore.getState().ask(' ¿Pregunta? ', true);
    expect(useAiStore.getState().messages.map((m) => m.role)).toEqual(['user', 'assistant']);
    expect(mockedAi.ask).toHaveBeenCalledWith('¿Pregunta?', true);

    mockedAi.ask.mockRejectedValueOnce({ response: { status: 429, data: { detail: 'Límite alcanzado' } } });
    await useAiStore.getState().ask('otra', false);
    const all = useAiStore.getState().messages;
    const last = all[all.length - 1];
    expect(last.error?.status).toBe(429);
    expect(useAiStore.getState().asking).toBe(false);

    useAiStore.getState().reset();
    expect(useAiStore.getState().messages).toHaveLength(0);
  });

  it('carga resumen de métricas', async () => {
    mockedAi.metrics.mockRejectedValueOnce({ response: { status: 403 } });
    await useAiStore.getState().loadSummary();
    expect(useAiStore.getState().summaryError?.status).toBe(403);
  });
});
