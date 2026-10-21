import { toApiError } from '../api/errors';
import { formatMs, formatPercent, formatUsd, formatDate } from '../utils/format';
import { isValid, validateEmail, validatePassword, validateQuestion, validateText } from '../utils/validation';

describe('validaciones de formulario', () => {
  it('valida email', () => {
    expect(validateEmail('ana@toka.com')).toBeNull();
    expect(validateEmail('')).toMatch(/obligatorio/);
    expect(validateEmail('ana@toka')).toMatch(/válido/);
    expect(validateEmail('<a>@toka.com')).toMatch(/válido/);
  });

  it.each([
    ['corta1!', /8 caracteres/],
    ['sinmayuscula1!', /mayúscula/],
    ['SINMINUSCULA1!', /minúscula/],
    ['SinDigitos!!', /dígito/],
    ['SinEspecial123', /especial/],
    ['', /obligatoria/]
  ])('detecta contraseña débil "%s"', (pwd, expected) => {
    expect(validatePassword(pwd)).toMatch(expected);
  });

  it('acepta contraseña fuerte', () => expect(validatePassword('Segura#2026')).toBeNull());

  it('valida texto libre y caracteres peligrosos', () => {
    expect(validateText('Ana', 'Nombre', 2, 10)).toBeNull();
    expect(validateText('', 'Nombre', 2, 10)).toMatch(/obligatorio/);
    expect(validateText('', 'Depto', 2, 10, false)).toBeNull();
    expect(validateText('A', 'Nombre', 2, 10)).toMatch(/al menos/);
    expect(validateText('x'.repeat(11), 'Nombre', 2, 10)).toMatch(/como máximo/);
    expect(validateText('<script>', 'Nombre', 2, 20)).toMatch(/no permitidos/);
  });

  it('valida pregunta del asistente', () => {
    expect(validateQuestion('hi')).not.toBeNull();
    expect(validateQuestion('x'.repeat(1001))).not.toBeNull();
    expect(validateQuestion('¿Qué es un rol?')).toBeNull();
  });

  it('isValid', () => {
    expect(isValid({ a: null, b: null })).toBe(true);
    expect(isValid({ a: null, b: 'error' })).toBe(false);
  });
});

describe('formato', () => {
  it('formatea métricas', () => {
    expect(formatMs(250)).toBe('250 ms');
    expect(formatMs(2500)).toBe('2.5 s');
    expect(formatUsd(0)).toBe('$0');
    expect(formatUsd(0.00028)).toBe('$0.000280');
    expect(formatUsd(0.5)).toBe('$0.5000');
    expect(formatPercent(0.756)).toBe('76%');
    expect(formatDate('no-fecha')).toBe('no-fecha');
  });
});

describe('toApiError', () => {
  it('normaliza ProblemDetails', () => {
    const err = toApiError({
      response: { status: 409, data: { title: 'auth.email_taken', detail: 'Ya existe', correlationId: 'abc' } }
    });
    expect(err).toEqual({ status: 409, code: 'auth.email_taken', message: 'Ya existe', correlationId: 'abc' });
  });

  it('normaliza errores OAuth y status sin cuerpo', () => {
    expect(toApiError({ response: { status: 400, data: { error: 'invalid_grant', error_description: 'Mal' } } }).code).toBe('invalid_grant');
    expect(toApiError({ response: { status: 503 } }).message).toMatch(/no disponible/);
    expect(toApiError({ response: { status: 418, headers: { 'x-correlation-id': 'h1' } } })).toMatchObject({ code: 'http_418', correlationId: 'h1' });
  });

  it('detecta error de red', () => {
    expect(toApiError(new Error('Network Error')).status).toBe(0);
    expect(toApiError(undefined).code).toBe('network_error');
  });
});
