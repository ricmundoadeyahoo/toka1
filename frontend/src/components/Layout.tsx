import type { ReactNode } from 'react';
import { Navigate, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../stores/authStore';
import { Permissions } from '../types';

/** Ruta protegida: exige sesión y, opcionalmente, un permiso. */
export function ProtectedRoute({ permission, children }: { permission?: string; children: ReactNode }) {
  const token = useAuthStore((s) => s.accessToken);
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const location = useLocation();

  if (!token) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  if (permission && !hasPermission(permission)) {
    return (
      <div className="card">
        <h2>Acceso restringido</h2>
        <p className="muted">Tu rol no incluye el permiso <code>{permission}</code>. Solicítalo a un administrador.</p>
      </div>
    );
  }
  return <>{children}</>;
}

export function Layout() {
  const user = useAuthStore((s) => s.user);
  const hasPermission = useAuthStore((s) => s.hasPermission);
  const logout = useAuthStore((s) => s.logout);
  const navigate = useNavigate();

  const links = [
    { to: '/perfil', label: 'Mi perfil', show: true },
    { to: '/usuarios', label: 'Usuarios', show: hasPermission(Permissions.UsersRead) },
    { to: '/roles', label: 'Roles', show: hasPermission(Permissions.RolesRead) },
    { to: '/auditoria', label: 'Auditoría', show: hasPermission(Permissions.AuditRead) },
    { to: '/asistente', label: 'Asistente IA', show: hasPermission(Permissions.AiUse) }
  ];

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark">T</span>
          <span>Toka · Usuarios</span>
        </div>
        <nav aria-label="Principal">
          {links
            .filter((l) => l.show)
            .map((l) => (
              <NavLink key={l.to} to={l.to} className={({ isActive }) => (isActive ? 'nav-link active' : 'nav-link')}>
                {l.label}
              </NavLink>
            ))}
        </nav>
        <div className="sidebar-footer">
          <div className="small">{user?.email}</div>
          <div className="chips">
            {user?.roles.map((r) => (
              <span key={r} className="chip">
                {r}
              </span>
            ))}
          </div>
          <button
            type="button"
            className="btn btn-ghost"
            onClick={async () => {
              await logout();
              navigate('/login');
            }}
          >
            Cerrar sesión
          </button>
        </div>
      </aside>
      <main className="content">
        <Outlet />
      </main>
    </div>
  );
}
