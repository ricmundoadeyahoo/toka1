import { useState, type FormEvent } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { ErrorBanner, Field } from '../components/common';
import { useAuthStore } from '../stores/authStore';
import { toast } from '../stores/toastStore';
import { isValid, validateEmail, validatePassword, validateText } from '../utils/validation';

export function LoginPage() {
  const { login, loading, error, accessToken, clearError } = useAuthStore();
  const navigate = useNavigate();
  const location = useLocation();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<Record<string, string | null>>({});

  if (accessToken) return <Navigate to="/perfil" replace />;

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    clearError();
    const next = { email: validateEmail(email), password: password ? null : 'La contraseña es obligatoria.' };
    setErrors(next);
    if (!isValid(next)) return;
    if (await login(email, password)) {
      toast.success('Bienvenido.');
      const from = (location.state as { from?: string } | null)?.from ?? '/perfil';
      navigate(from, { replace: true });
    }
  };

  return (
    <div className="auth-wrap">
      <form className="card auth-card" onSubmit={onSubmit} noValidate aria-label="Iniciar sesión">
        <h1>Iniciar sesión</h1>
        <p className="muted">Sistema de gestión de usuarios</p>
        <ErrorBanner error={error} />
        <Field id="email" label="Email" value={email} onChange={setEmail} error={errors.email} autoComplete="username" />
        <Field id="password" label="Contraseña" type="password" value={password} onChange={setPassword} error={errors.password} autoComplete="current-password" />
        <button type="submit" className="btn btn-primary" disabled={loading}>
          {loading ? 'Ingresando…' : 'Ingresar'}
        </button>
        <p className="small">
          ¿No tienes cuenta? <Link to="/registro">Crear cuenta</Link>
        </p>
      </form>
    </div>
  );
}

export function RegisterPage() {
  const { register, loading, error, clearError } = useAuthStore();
  const navigate = useNavigate();
  const [form, setForm] = useState({ fullName: '', email: '', department: '', password: '', confirm: '' });
  const [errors, setErrors] = useState<Record<string, string | null>>({});
  const set = (key: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [key]: value }));

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    clearError();
    const next = {
      fullName: validateText(form.fullName, 'El nombre', 2, 120),
      email: validateEmail(form.email),
      department: validateText(form.department, 'El departamento', 2, 80, false),
      password: validatePassword(form.password),
      confirm: form.confirm === form.password ? null : 'Las contraseñas no coinciden.'
    };
    setErrors(next);
    if (!isValid(next)) return;
    const ok = await register({
      fullName: form.fullName.trim(),
      email: form.email.trim(),
      password: form.password,
      department: form.department.trim() || undefined
    });
    if (ok) {
      toast.success('Cuenta creada. Ya puedes iniciar sesión.');
      navigate('/login');
    }
  };

  return (
    <div className="auth-wrap">
      <form className="card auth-card" onSubmit={onSubmit} noValidate aria-label="Crear cuenta">
        <h1>Crear cuenta</h1>
        <ErrorBanner error={error} />
        <Field id="fullName" label="Nombre completo" value={form.fullName} onChange={set('fullName')} error={errors.fullName} />
        <Field id="email" label="Email" value={form.email} onChange={set('email')} error={errors.email} autoComplete="email" />
        <Field id="department" label="Departamento (opcional)" value={form.department} onChange={set('department')} error={errors.department} />
        <Field id="password" label="Contraseña" type="password" value={form.password} onChange={set('password')} error={errors.password} autoComplete="new-password" />
        <Field id="confirm" label="Confirmar contraseña" type="password" value={form.confirm} onChange={set('confirm')} error={errors.confirm} autoComplete="new-password" />
        <p className="small muted">Mínimo 8 caracteres con mayúscula, minúscula, dígito y carácter especial.</p>
        <button type="submit" className="btn btn-primary" disabled={loading}>
          {loading ? 'Creando…' : 'Crear cuenta'}
        </button>
        <p className="small">
          <Link to="/login">Ya tengo cuenta</Link>
        </p>
      </form>
    </div>
  );
}
