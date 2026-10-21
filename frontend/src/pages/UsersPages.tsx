import { useEffect, useState, type FormEvent } from 'react';
import { usersApi } from '../api/endpoints';
import { toApiError } from '../api/errors';
import { EmptyState, ErrorBanner, Field, Pager, Spinner } from '../components/common';
import { useAuthStore } from '../stores/authStore';
import { toast } from '../stores/toastStore';
import { useUsersStore } from '../stores/usersStore';
import { Permissions, type ApiError, type User } from '../types';
import { formatDate } from '../utils/format';
import { isValid, validateText } from '../utils/validation';

interface EditFormProps {
  user: User;
  saving: boolean;
  onSave: (data: { fullName: string; department: string | null; jobTitle: string | null }) => Promise<boolean>;
  onCancel?: () => void;
}

/** Formulario de edición con validación en cliente (reutilizado en Perfil y Usuarios). */
export function UserEditForm({ user, saving, onSave, onCancel }: EditFormProps) {
  const [fullName, setFullName] = useState(user.fullName);
  const [department, setDepartment] = useState(user.department ?? '');
  const [jobTitle, setJobTitle] = useState(user.jobTitle ?? '');
  const [errors, setErrors] = useState<Record<string, string | null>>({});

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const next = {
      fullName: validateText(fullName, 'El nombre', 2, 120),
      department: validateText(department, 'El departamento', 2, 80, false),
      jobTitle: validateText(jobTitle, 'El puesto', 2, 80, false)
    };
    setErrors(next);
    if (!isValid(next)) return;
    await onSave({ fullName: fullName.trim(), department: department.trim() || null, jobTitle: jobTitle.trim() || null });
  };

  return (
    <form className="edit-form" onSubmit={submit} noValidate aria-label={`Editar ${user.email}`}>
      <Field id={`name-${user.id}`} label="Nombre" value={fullName} onChange={setFullName} error={errors.fullName} />
      <Field id={`dept-${user.id}`} label="Departamento" value={department} onChange={setDepartment} error={errors.department} />
      <Field id={`job-${user.id}`} label="Puesto" value={jobTitle} onChange={setJobTitle} error={errors.jobTitle} />
      <div className="row">
        <button type="submit" className="btn btn-primary" disabled={saving}>
          {saving ? 'Guardando…' : 'Guardar'}
        </button>
        {onCancel && (
          <button type="button" className="btn btn-ghost" onClick={onCancel}>
            Cancelar
          </button>
        )}
      </div>
    </form>
  );
}

export function UsersPage() {
  const { items, totalCount, totalPages, query, loading, saving, error, fetch, update, toggleStatus } = useUsersStore();
  const canWrite = useAuthStore((s) => s.hasPermission(Permissions.UsersWrite));
  const currentUserId = useAuthStore((s) => s.user?.id);
  const [search, setSearch] = useState(query.search ?? '');
  const [editing, setEditing] = useState<string | null>(null);

  useEffect(() => {
    void fetch({ page: 1 });
  }, [fetch]);

  return (
    <section>
      <header className="page-header">
        <h1>Usuarios</h1>
        <span className="muted">{totalCount} en total</span>
      </header>

      <form
        className="toolbar"
        role="search"
        onSubmit={(e) => {
          e.preventDefault();
          void fetch({ search: search.trim() || undefined, page: 1 });
        }}
      >
        <input aria-label="Buscar usuarios" placeholder="Buscar por nombre o email" value={search} onChange={(e) => setSearch(e.target.value)} maxLength={100} />
        <select aria-label="Filtrar por estado" value={query.status ?? ''} onChange={(e) => void fetch({ status: e.target.value || undefined, page: 1 })}>
          <option value="">Todos</option>
          <option value="Active">Activos</option>
          <option value="Deactivated">Desactivados</option>
        </select>
        <button type="submit" className="btn">Buscar</button>
      </form>

      <ErrorBanner error={error} onRetry={() => void fetch()} />
      {loading && <Spinner label="Cargando usuarios…" />}
      {!loading && !error && items.length === 0 && <EmptyState>No se encontraron usuarios.</EmptyState>}

      {!loading && items.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Nombre</th>
                <th>Email</th>
                <th>Departamento</th>
                <th>Estado</th>
                <th>Alta</th>
                {canWrite && <th aria-label="Acciones" />}
              </tr>
            </thead>
            <tbody>
              {items.map((u) =>
                editing === u.id ? (
                  <tr key={u.id}>
                    <td colSpan={canWrite ? 6 : 5}>
                      <UserEditForm
                        user={u}
                        saving={saving}
                        onCancel={() => setEditing(null)}
                        onSave={async (data) => {
                          const ok = await update(u.id, data);
                          if (ok) setEditing(null);
                          return ok;
                        }}
                      />
                    </td>
                  </tr>
                ) : (
                  <tr key={u.id}>
                    <td>
                      {u.fullName}
                      <div className="mono small muted" title="Id del usuario">{u.id}</div>
                    </td>
                    <td className="mono">{u.email}</td>
                    <td>{u.department ?? '—'}</td>
                    <td>
                      <span className={`badge ${u.status === 'Active' ? 'badge-ok' : 'badge-off'}`}>{u.status === 'Active' ? 'Activo' : 'Desactivado'}</span>
                    </td>
                    <td>{formatDate(u.createdAt)}</td>
                    {canWrite && (
                      <td className="actions">
                        <button type="button" className="btn btn-ghost" onClick={() => setEditing(u.id)}>
                          Editar
                        </button>
                        {u.id !== currentUserId && (
                          <button type="button" className="btn btn-ghost" disabled={saving} onClick={() => void toggleStatus(u)}>
                            {u.status === 'Active' ? 'Desactivar' : 'Reactivar'}
                          </button>
                        )}
                      </td>
                    )}
                  </tr>
                )
              )}
            </tbody>
          </table>
        </div>
      )}
      <Pager page={query.page} totalPages={totalPages} onChange={(page) => void fetch({ page })} />
    </section>
  );
}

export function ProfilePage() {
  const [profile, setProfile] = useState<User | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [saving, setSaving] = useState(false);
  const session = useAuthStore((s) => s.user);

  const load = async () => {
    setError(null);
    try {
      setProfile(await usersApi.me());
    } catch (e) {
      setError(toApiError(e));
    }
  };

  useEffect(() => {
    void load();
  }, []);

  return (
    <section>
      <header className="page-header">
        <h1>Mi perfil</h1>
      </header>
      <ErrorBanner error={error} onRetry={() => void load()} />
      {!profile && !error && <Spinner />}
      {profile && (
        <div className="grid-2">
          <div className="card">
            <h2>{profile.fullName}</h2>
            <p className="mono">{profile.email}</p>
            <p className="muted small">Roles: {session?.roles.join(', ') || '—'}</p>
            <p className="muted small">Permisos: {session?.permissions.join(', ') || '—'}</p>
          </div>
          <div className="card">
            <h2>Editar datos</h2>
            <UserEditForm
              user={profile}
              saving={saving}
              onSave={async (data) => {
                setSaving(true);
                try {
                  setProfile(await usersApi.update(profile.id, data));
                  toast.success('Perfil actualizado.');
                  return true;
                } catch (e) {
                  toast.error(toApiError(e).message);
                  return false;
                } finally {
                  setSaving(false);
                }
              }}
            />
          </div>
        </div>
      )}
    </section>
  );
}
