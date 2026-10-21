/** Validaciones de formulario en cliente (UX). El backend vuelve a validar todo (nunca confiar en el cliente). */

const EMAIL_REGEX = /^[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+$/;
const UNSAFE_REGEX = /[<>]/;

export function validateEmail(value: string): string | null {
  const v = value.trim();
  if (!v) return 'El email es obligatorio.';
  if (v.length > 254 || !EMAIL_REGEX.test(v)) return 'Ingresa un email válido.';
  return null;
}

export function validatePassword(value: string): string | null {
  if (!value) return 'La contraseña es obligatoria.';
  if (value.length < 8) return 'Debe tener al menos 8 caracteres.';
  if (value.length > 128) return 'Debe tener como máximo 128 caracteres.';
  if (!/[A-Z]/.test(value)) return 'Debe incluir una mayúscula.';
  if (!/[a-z]/.test(value)) return 'Debe incluir una minúscula.';
  if (!/[0-9]/.test(value)) return 'Debe incluir un dígito.';
  if (!/[^A-Za-z0-9]/.test(value)) return 'Debe incluir un carácter especial.';
  return null;
}

export function validateText(value: string | null | undefined, label: string, min: number, max: number, required = true): string | null {
  const v = (value ?? '').trim();
  if (!v) return required ? `${label} es obligatorio.` : null;
  if (v.length < min) return `${label} debe tener al menos ${min} caracteres.`;
  if (v.length > max) return `${label} debe tener como máximo ${max} caracteres.`;
  if (UNSAFE_REGEX.test(v)) return `${label} contiene caracteres no permitidos.`;
  return null;
}

export function validateQuestion(value: string): string | null {
  const v = value.trim();
  if (v.length < 3) return 'Escribe una pregunta de al menos 3 caracteres.';
  if (v.length > 1000) return 'La pregunta no puede exceder 1000 caracteres.';
  return null;
}

/** Devuelve true si no hay errores. */
export function isValid(errors: Record<string, string | null>): boolean {
  return Object.values(errors).every((e) => e === null);
}
