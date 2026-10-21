import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import App from '../App';
import { aiApi, authApi, usersApi } from '../api/endpoints';
import { useAiStore } from '../stores/aiStore';
import { useAuthStore } from '../stores/authStore';
import { useUsersStore } from '../stores/usersStore';
import type { AskResponse, User } from '../types';

jest.mock('../api/endpoints');

const mockedAuth = authApi as jest.Mocked<typeof authApi>;
const mockedUsers = usersApi as jest.Mocked<typeof usersApi>;
const mockedAi = aiApi as jest.Mocked<typeof aiApi>;

const adminUser = { id: 'u1', email: 'admin@toka.local', roles: ['Admin'], permissions: ['users:read', 'users:write', 'ai:use', 'ai:admin', 'roles:read', 'audit:read'] };
const ana: User = {
  id: 'u2', email: 'ana@toka.com', fullName: 'Ana López', department: 'Finanzas', jobTitle: null,
  status: 'Active', createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z'
};

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]} future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <App />
    </MemoryRouter>
  );
}

function loginAs(user = adminUser) {
  useAuthStore.setState({ accessToken: 'at', refreshToken: 'rt', user });
}

beforeEach(() => {
  jest.resetAllMocks();
  sessionStorage.clear();
  useAuthStore.setState({ accessToken: null, refreshToken: null, user: null, loading: false, error: null });
  useUsersStore.setState({ items: [], totalCount: 0, totalPages: 0, query: { page: 1, pageSize: 10 }, loading: false, error: null });
  useAiStore.setState({ messages: [], asking: false, summary: null, summaryError: null });
});

describe('LoginPage', () => {
  it('valida el formulario antes de llamar a la API', async () => {
    renderAt('/login');
    await userEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
    expect(screen.getByText('El email es obligatorio.')).toBeInTheDocument();
    expect(screen.getByText('La contraseña es obligatoria.')).toBeInTheDocument();
    expect(mockedAuth.login).not.toHaveBeenCalled();
  });

  it('muestra el error del servidor', async () => {
    mockedAuth.login.mockRejectedValue({ response: { status: 401, data: { detail: 'Email o contraseña incorrectos.', correlationId: 'c-1' } } });
    renderAt('/login');
    await userEvent.type(screen.getByLabelText('Email'), 'ana@toka.com');
    await userEvent.type(screen.getByLabelText('Contraseña'), 'Mala#2026');
    await userEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Email o contraseña incorrectos.');
    expect(screen.getByText(/c-1/)).toBeInTheDocument();
  });

  it('inicia sesión y redirige al perfil', async () => {
    mockedAuth.login.mockResolvedValue({ accessToken: 'at', tokenType: 'Bearer', expiresIn: 900, refreshToken: 'rt', user: adminUser });
    mockedUsers.me.mockResolvedValue({ ...ana, id: 'u1', email: 'admin@toka.local', fullName: 'Administrador Toka' });
    renderAt('/login');
    await userEvent.type(screen.getByLabelText('Email'), 'admin@toka.local');
    await userEvent.type(screen.getByLabelText('Contraseña'), 'Admin123!');
    await userEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
    expect(await screen.findByRole('heading', { name: 'Administrador Toka' })).toBeInTheDocument();
  });
});

describe('RegisterPage', () => {
  it('valida contraseña y confirmación', async () => {
    renderAt('/registro');
    await userEvent.type(screen.getByLabelText('Nombre completo'), 'Ana López');
    await userEvent.type(screen.getByLabelText('Email'), 'ana@toka.com');
    await userEvent.type(screen.getByLabelText('Contraseña'), 'debil');
    await userEvent.type(screen.getByLabelText('Confirmar contraseña'), 'otra');
    await userEvent.click(screen.getByRole('button', { name: 'Crear cuenta' }));
    expect(screen.getByText('Debe tener al menos 8 caracteres.')).toBeInTheDocument();
    expect(screen.getByText('Las contraseñas no coinciden.')).toBeInTheDocument();
    expect(mockedAuth.register).not.toHaveBeenCalled();
  });
});

describe('Rutas protegidas', () => {
  it('redirige a login sin sesión', () => {
    renderAt('/usuarios');
    expect(screen.getByRole('heading', { name: 'Iniciar sesión' })).toBeInTheDocument();
  });

  it('bloquea pantallas sin permiso', () => {
    loginAs({ ...adminUser, roles: ['User'], permissions: ['ai:use'] });
    mockedUsers.me.mockResolvedValue(ana);
    renderAt('/auditoria');
    expect(screen.getByRole('heading', { name: 'Acceso restringido' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Usuarios' })).not.toBeInTheDocument();
  });
});

describe('UsersPage', () => {
  it('muestra loading, lista usuarios y permite desactivar', async () => {
    loginAs();
    mockedUsers.list.mockResolvedValue({ items: [ana], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 });
    mockedUsers.deactivate.mockResolvedValue({ ...ana, status: 'Deactivated' });

    renderAt('/usuarios');
    expect(screen.getByText('Cargando usuarios…')).toBeInTheDocument();
    expect(await screen.findByText('Ana López')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Desactivar' }));
    await waitFor(() => expect(screen.getByText('Desactivado')).toBeInTheDocument());
    expect(screen.getByText('Usuario desactivado.')).toBeInTheDocument();
  });

  it('muestra estado vacío y error con reintento', async () => {
    loginAs();
    mockedUsers.list.mockRejectedValueOnce({ response: { status: 503 } });
    renderAt('/usuarios');
    expect(await screen.findByRole('alert')).toHaveTextContent('Servicio temporalmente no disponible.');

    mockedUsers.list.mockResolvedValueOnce({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 });
    await userEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByText('No se encontraron usuarios.')).toBeInTheDocument();
  });

  it('valida la edición en línea', async () => {
    loginAs();
    mockedUsers.list.mockResolvedValue({ items: [ana], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 });
    renderAt('/usuarios');
    await userEvent.click(await screen.findByRole('button', { name: 'Editar' }));
    const name = screen.getByLabelText('Nombre');
    await userEvent.clear(name);
    await userEvent.type(name, '<b>');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar' }));
    expect(screen.getByText('El nombre contiene caracteres no permitidos.')).toBeInTheDocument();
    expect(mockedUsers.update).not.toHaveBeenCalled();
  });
});

describe('AssistantPage', () => {
  it('pregunta al agente y muestra fuentes, métricas y calidad', async () => {
    loginAs({ ...adminUser, permissions: ['ai:use'] });
    const response: AskResponse = {
      queryId: 'q1',
      answer: 'La cuenta se bloquea tras 5 intentos [1].',
      sources: [{ index: 1, title: 'Política de contraseñas', source: 'docs/02.md', score: 0.81 }],
      toolsUsed: [],
      metrics: {
        latencyMs: 2300, retrievalMs: 40, llmMs: 2200, toolMs: 0, llmCalls: 1, promptTokens: 620, cachedPromptTokens: 512, completionTokens: 48,
        embeddingTokens: 12, totalTokens: 680, estimatedCostUsd: 0.000093, referenceCostUsd: 0.000122, referenceModel: 'gpt-4o-mini',
        model: 'deepseek-flash', promptVersion: 'v1.2', cacheHit: false
      },
      quality: { score: 1, passed: true, checks: [{ name: 'fundamentada', passed: true, detail: '100%', weight: 2 }] },
      possibleInjection: false
    };
    mockedAi.ask.mockResolvedValue(response);

    renderAt('/asistente');
    await userEvent.type(screen.getByLabelText('Pregunta para el asistente'), '¿Cuántos intentos fallidos se permiten?');
    await userEvent.click(screen.getByRole('button', { name: 'Preguntar' }));

    expect(await screen.findByText('La cuenta se bloquea tras 5 intentos [1].')).toBeInTheDocument();
    expect(screen.getByText(/Política de contraseñas/)).toBeInTheDocument();
    expect(screen.getByText('2.3 s')).toBeInTheDocument();
    expect(screen.getByText('620/48')).toBeInTheDocument();
    expect(screen.getByText('512')).toBeInTheDocument();
    expect(screen.getByText('$0.000093')).toBeInTheDocument();
    expect(screen.getByText('100%')).toBeInTheDocument();
    expect(mockedAi.ask).toHaveBeenCalledWith('¿Cuántos intentos fallidos se permiten?', true);
  });

  it('valida pregunta vacía', async () => {
    loginAs({ ...adminUser, permissions: ['ai:use'] });
    renderAt('/asistente');
    await userEvent.click(screen.getByRole('button', { name: 'Preguntar' }));
    expect(screen.getByText('Escribe una pregunta de al menos 3 caracteres.')).toBeInTheDocument();
    expect(mockedAi.ask).not.toHaveBeenCalled();
  });
});
