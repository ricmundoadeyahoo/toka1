import type { ApiError } from '../types';

interface AxiosLikeError {
  isAxiosError?: boolean;
  message?: string;
  code?: string;
  response?: { status: number; data?: unknown; headers?: Record<string, string> };
}

const statusMessages: Record<number, string> = {
  0: 'No hay conexión con el servidor. Verifica que los servicios estén levantados.',
  400: 'La solicitud no es válida.',
  401: 'Tu sesión expiró o las credenciales son inválidas.',
  403: 'No tienes permisos para realizar esta acción.',
  404: 'El recurso no existe.',
  409: 'Conflicto con el estado actual del recurso.',
  429: 'Demasiadas solicitudes. Espera un momento e intenta de nuevo.',
  500: 'Error interno del servidor.',
  502: 'Error de comunicación entre servicios.',
  503: 'Servicio temporalmente no disponible.',
  504: 'El servicio tardó demasiado en responder.'
};

/** Convierte cualquier error (axios, ProblemDetails, OAuth) a un ApiError homogéneo para la UI. */
export function toApiError(error: unknown): ApiError {
  const e = error as AxiosLikeError;
  if (!e || !e.response) {
    return { status: 0, code: e?.code ?? 'network_error', message: statusMessages[0] };
  }

  const { status, data, headers } = e.response;
  const body = (data ?? {}) as Record<string, unknown>;
  const message =
    (typeof body.detail === 'string' && body.detail) ||
    (typeof body.error_description === 'string' && body.error_description) ||
    statusMessages[status] ||
    'Ocurrió un error inesperado.';
  const code =
    (typeof body.code === 'string' && body.code) ||
    (typeof body.title === 'string' && body.title) ||
    (typeof body.error === 'string' && body.error) ||
    `http_${status}`;
  const correlationId =
    (typeof body.correlationId === 'string' && body.correlationId) || headers?.['x-correlation-id'] || undefined;

  return { status, code, message, correlationId };
}
