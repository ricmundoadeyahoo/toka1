import { useEffect, useState } from 'react';
import { EmptyState, ErrorBanner, Pager, Spinner } from '../components/common';
import { useAuditStore } from '../stores/auditStore';
import { useAuthStore } from '../stores/authStore';
import { useRolesStore } from '../stores/rolesStore';
import { Permissions } from '../types';
import { formatDate } from '../utils/format';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function RolesPage() {
  const { roles, selected, loading, error, fetchRoles, loadUserRoles, assign, revoke } = useRolesStore();
  const canManage = useAuthStore((s) => s.hasPermission(Permissions.RolesManage));
  const [userId, setUserId] = useState('');
  const [roleToAssign, setRoleToAssign] = useState('');
  const [idError, setIdError] = useState<string | null>(null);

  useEffect(() => {
    void fetchRoles();
  }, [fetchRoles]);

  return (
    <section>
      <header className="page-header">
        <h1>Roles y permisos</h1>
      </header>
      <ErrorBanner error={error} onRetry={() => void fetchRoles()} />
      {loading && roles.length === 0 && <Spinner />}

      <div className="grid-3">
        {roles.map((r) => (
          <article key={r.id} className="card">
            <h3>
              {r.name} {r.isSystem && <span className="chip">sistema</span>}
            </h3>
            <p className="muted small">{r.description}</p>
            <div className="chips">
              {r.permissions.map((p) => (
                <code key={p} className="chip">
                  {p}
                </code>
              ))}
            </div>
          </article>
        ))}
      </div>

      {canManage && (
        <div className="card">
          <h2>Asignar roles a un usuario</h2>
          <form
            className="toolbar"
            onSubmit={(e) => {
              e.preventDefault();
              if (!GUID.test(userId.trim())) {
                setIdError('Ingresa un id de usuario válido (GUID). Lo encuentras en la pantalla Usuarios o en Auditoría.');
                return;
              }
              setIdError(null);
              void loadUserRoles(userId.trim());
            }}
          >
            <input aria-label="Id de usuario" placeholder="Id del usuario (GUID)" value={userId} onChange={(e) => setUserId(e.target.value)} />
            <button type="submit" className="btn">Consultar</button>
          </form>
          {idError && <p className="field-error">{idError}</p>}

          {selected && (
            <div className="stack">
              <div className="chips">
                {selected.roles.length === 0 && <span className="muted">Sin roles</span>}
                {selected.roles.map((r) => (
                  <span key={r} className="chip chip-action">
                    {r}
                    <button type="button" aria-label={`Revocar ${r}`} onClick={() => void revoke(selected.userId, r)}>
                      ×
                    </button>
                  </span>
                ))}
              </div>
              <div className="toolbar">
                <select aria-label="Rol a asignar" value={roleToAssign} onChange={(e) => setRoleToAssign(e.target.value)}>
                  <option value="">Selecciona un rol…</option>
                  {roles
                    .filter((r) => !selected.roles.includes(r.name))
                    .map((r) => (
                      <option key={r.id} value={r.name}>
                        {r.name}
                      </option>
                    ))}
                </select>
                <button
                  type="button"
                  className="btn btn-primary"
                  disabled={!roleToAssign}
                  onClick={async () => {
                    if (await assign(selected.userId, roleToAssign)) setRoleToAssign('');
                  }}
                >
                  Asignar
                </button>
              </div>
              <p className="muted small">Permisos efectivos: {selected.permissions.join(', ') || '—'}</p>
            </div>
          )}
        </div>
      )}
    </section>
  );
}

const EVENT_TYPES = [
  'identity.user.registered',
  'identity.login.succeeded',
  'identity.login.failed',
  'identity.account.locked',
  'users.user.updated',
  'users.user.deactivated',
  'roles.assignment.granted',
  'roles.assignment.revoked',
  'ai.query.completed'
];

export function AuditPage() {
  const { items, totalCount, totalPages, query, loading, error, fetch } = useAuditStore();

  useEffect(() => {
    void fetch({ page: 1 });
  }, [fetch]);

  return (
    <section>
      <header className="page-header">
        <h1>Auditoría</h1>
        <span className="muted">{totalCount} eventos</span>
      </header>
      <div className="toolbar">
        <select aria-label="Tipo de evento" value={query.eventType ?? ''} onChange={(e) => void fetch({ eventType: e.target.value || undefined, page: 1 })}>
          <option value="">Todos los eventos</option>
          {EVENT_TYPES.map((t) => (
            <option key={t} value={t}>
              {t}
            </option>
          ))}
        </select>
        <select
          aria-label="Resultado"
          value={query.success === undefined ? '' : String(query.success)}
          onChange={(e) => void fetch({ success: e.target.value === '' ? undefined : e.target.value === 'true', page: 1 })}
        >
          <option value="">Éxito y fallo</option>
          <option value="true">Solo éxito</option>
          <option value="false">Solo fallos</option>
        </select>
        <button type="button" className="btn" onClick={() => void fetch()}>
          Actualizar
        </button>
      </div>
      <ErrorBanner error={error} onRetry={() => void fetch()} />
      {loading && <Spinner label="Cargando auditoría…" />}
      {!loading && !error && items.length === 0 && <EmptyState>Sin eventos para los filtros seleccionados.</EmptyState>}
      {!loading && items.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Fecha</th>
                <th>Evento</th>
                <th>Actor</th>
                <th>Sujeto</th>
                <th>Resultado</th>
                <th>Detalles</th>
              </tr>
            </thead>
            <tbody>
              {items.map((e) => (
                <tr key={e.id}>
                  <td>{formatDate(e.occurredAt)}</td>
                  <td className="mono">{e.eventType}</td>
                  <td>{e.actorEmail ?? e.actorId ?? '—'}</td>
                  <td className="mono small">{e.subjectId ?? '—'}</td>
                  <td>
                    <span className={`badge ${e.success ? 'badge-ok' : 'badge-off'}`}>{e.success ? 'Éxito' : 'Fallo'}</span>
                  </td>
                  <td className="small">
                    {Object.entries(e.details)
                      .map(([k, v]) => `${k}: ${v}`)
                      .join(' · ') || '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <Pager page={query.page} totalPages={totalPages} onChange={(page) => void fetch({ page })} />
    </section>
  );
}
