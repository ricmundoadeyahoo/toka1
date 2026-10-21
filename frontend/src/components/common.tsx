import type { ReactNode } from 'react';
import { useToastStore } from '../stores/toastStore';
import type { ApiError } from '../types';

export function Spinner({ label = 'Cargando…' }: { label?: string }) {
  return (
    <div className="spinner" role="status" aria-live="polite">
      <span className="spinner-dot" aria-hidden="true" />
      <span>{label}</span>
    </div>
  );
}

export function ErrorBanner({ error, onRetry }: { error: ApiError | null; onRetry?: () => void }) {
  if (!error) return null;
  return (
    <div className="banner banner-error" role="alert">
      <div>
        <strong>{error.message}</strong>
        {error.correlationId && <div className="muted small">Correlation ID: {error.correlationId}</div>}
      </div>
      {onRetry && (
        <button type="button" className="btn btn-ghost" onClick={onRetry}>
          Reintentar
        </button>
      )}
    </div>
  );
}

export function EmptyState({ children }: { children: ReactNode }) {
  return <div className="empty">{children}</div>;
}

interface FieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string | null;
  type?: string;
  autoComplete?: string;
  placeholder?: string;
}

export function Field({ id, label, value, onChange, error, type = 'text', autoComplete, placeholder }: FieldProps) {
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <input
        id={id}
        type={type}
        value={value}
        autoComplete={autoComplete}
        placeholder={placeholder}
        aria-invalid={Boolean(error)}
        aria-describedby={error ? `${id}-error` : undefined}
        onChange={(e) => onChange(e.target.value)}
      />
      {error && (
        <span id={`${id}-error`} className="field-error">
          {error}
        </span>
      )}
    </div>
  );
}

export function Pager({ page, totalPages, onChange }: { page: number; totalPages: number; onChange: (page: number) => void }) {
  if (totalPages <= 1) return null;
  return (
    <nav className="pager" aria-label="Paginación">
      <button type="button" className="btn btn-ghost" disabled={page <= 1} onClick={() => onChange(page - 1)}>
        ← Anterior
      </button>
      <span>
        Página {page} de {totalPages}
      </span>
      <button type="button" className="btn btn-ghost" disabled={page >= totalPages} onClick={() => onChange(page + 1)}>
        Siguiente →
      </button>
    </nav>
  );
}

export function Toasts() {
  const { toasts, dismiss } = useToastStore();
  return (
    <div className="toasts" aria-live="polite">
      {toasts.map((t) => (
        <div key={t.id} className={`toast toast-${t.kind}`} role="status">
          <span>{t.message}</span>
          <button type="button" aria-label="Cerrar" onClick={() => dismiss(t.id)}>
            ×
          </button>
        </div>
      ))}
    </div>
  );
}
