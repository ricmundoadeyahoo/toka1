export function formatDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString('es-MX', { dateStyle: 'short', timeStyle: 'short' });
}

export function formatMs(ms: number): string {
  return ms >= 1000 ? `${(ms / 1000).toFixed(1)} s` : `${Math.round(ms)} ms`;
}

export function formatUsd(value: number): string {
  if (value === 0) return '$0';
  return value < 0.01 ? `$${value.toFixed(6)}` : `$${value.toFixed(4)}`;
}

export function formatPercent(ratio: number): string {
  return `${Math.round(ratio * 100)}%`;
}
