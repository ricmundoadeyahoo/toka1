import { useEffect, useRef, useState, type FormEvent } from 'react';
import { ErrorBanner, Spinner } from '../components/common';
import { useAiStore, type ChatMessage } from '../stores/aiStore';
import { useAuthStore } from '../stores/authStore';
import { Permissions } from '../types';
import { formatMs, formatPercent, formatUsd } from '../utils/format';
import { validateQuestion } from '../utils/validation';

const SUGGESTIONS = [
  '¿Cuántos intentos fallidos se permiten antes de bloquear una cuenta?',
  '¿Qué permisos tiene el rol Auditor?',
  '¿Cuántos usuarios activos hay por departamento?',
  'Resume los inicios de sesión fallidos de las últimas 24 horas'
];

function AssistantMessage({ message }: { message: ChatMessage }) {
  const r = message.response;
  return (
    <div className={`msg msg-assistant${message.error ? ' msg-error' : ''}`}>
      <p className="msg-text">{message.content}</p>
      {message.error?.correlationId && <p className="muted small">Correlation ID: {message.error.correlationId}</p>}
      {r && (
        <>
          {r.possibleInjection && <p className="banner banner-warn small">Se detectó un posible intento de prompt injection; la respuesta se generó con reglas reforzadas.</p>}
          {r.sources.length > 0 && (
            <p className="small">
              Fuentes:{' '}
              {r.sources.map((s) => (
                <span key={s.index} className="chip" title={`${s.source} · relevancia ${s.score}`}>
                  [{s.index}] {s.title}
                </span>
              ))}
            </p>
          )}
          {r.toolsUsed.length > 0 && <p className="small muted">Herramientas: {r.toolsUsed.join(', ')}</p>}
          <dl className="metrics" aria-label="Métricas de la respuesta">
            <div>
              <dt>Latencia</dt>
              <dd>{formatMs(r.metrics.latencyMs)}</dd>
            </div>
            <div>
              <dt>Tokens (in/out)</dt>
              <dd>
                {r.metrics.promptTokens}/{r.metrics.completionTokens}
              </dd>
            </div>
            <div>
              <dt title="Tokens de entrada servidos desde la caché de contexto del proveedor">Entrada en caché</dt>
              <dd>{r.metrics.cachedPromptTokens ?? 0}</dd>
            </div>
            <div>
              <dt>Costo real</dt>
              <dd>{formatUsd(r.metrics.estimatedCostUsd)}</dd>
            </div>
            <div>
              <dt title={r.metrics.referenceModel}>Costo ref. nube</dt>
              <dd>{formatUsd(r.metrics.referenceCostUsd)}</dd>
            </div>
            <div>
              <dt>Calidad</dt>
              <dd className={r.quality.passed ? 'ok' : 'warn'}>{formatPercent(r.quality.score)}</dd>
            </div>
            <div>
              <dt>Caché</dt>
              <dd>{r.metrics.cacheHit ? 'sí' : 'no'}</dd>
            </div>
          </dl>
          <details className="small">
            <summary>Validaciones de calidad</summary>
            <ul>
              {r.quality.checks.map((c) => (
                <li key={c.name}>
                  {c.passed ? '✅' : '⚠️'} <code>{c.name}</code> — {c.detail}
                </li>
              ))}
            </ul>
            <p className="muted">
              Modelo {r.metrics.model} · prompt {r.metrics.promptVersion} · {r.metrics.llmCalls} llamada(s) al LLM · recuperación {formatMs(r.metrics.retrievalMs)}
            </p>
          </details>
        </>
      )}
    </div>
  );
}

export function AssistantPage() {
  const { messages, asking, ask, summary, summaryError, loadSummary, reset } = useAiStore();
  const isAiAdmin = useAuthStore((s) => s.hasPermission(Permissions.AiAdmin));
  const [question, setQuestion] = useState('');
  const [useCache, setUseCache] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (isAiAdmin) void loadSummary();
  }, [isAiAdmin, loadSummary]);

  useEffect(() => {
    endRef.current?.scrollIntoView?.({ behavior: 'smooth' });
  }, [messages.length]);

  const submit = async (e?: FormEvent, text = question) => {
    e?.preventDefault();
    const validation = validateQuestion(text);
    setError(validation);
    if (validation) return;
    setQuestion('');
    await ask(text, useCache);
    if (isAiAdmin) void loadSummary();
  };

  return (
    <section>
      <header className="page-header">
        <h1>Asistente IA</h1>
        <span className="muted small">DeepSeek + RAG · respuestas con fuentes, costo y métricas</span>
      </header>

      {isAiAdmin && summary && (
        <div className="kpis" aria-label="Métricas agregadas del agente">
          <div className="kpi"><span>Consultas</span><strong>{summary.queries}</strong></div>
          <div className="kpi"><span>Latencia p95</span><strong>{formatMs(summary.p95LatencyMs)}</strong></div>
          <div className="kpi"><span>Tokens/consulta</span><strong>{Math.round(summary.avgTokensPerQuery)}</strong></div>
          <div className="kpi"><span>Costo ref. total</span><strong>{formatUsd(summary.totalReferenceCostUsd)}</strong></div>
          <div className="kpi"><span>Cache hit</span><strong>{formatPercent(summary.cacheHitRate)}</strong></div>
          <div className="kpi"><span>Calidad OK</span><strong>{formatPercent(summary.qualityPassRate)}</strong></div>
        </div>
      )}
      {isAiAdmin && <ErrorBanner error={summaryError} />}

      <div className="chat card">
        {messages.length === 0 && (
          <div className="suggestions">
            <p className="muted">Prueba con:</p>
            {SUGGESTIONS.map((s) => (
              <button key={s} type="button" className="btn btn-ghost suggestion" disabled={asking} onClick={() => void submit(undefined, s)}>
                {s}
              </button>
            ))}
          </div>
        )}
        {messages.map((m) =>
          m.role === 'user' ? (
            <div key={m.id} className="msg msg-user">
              {m.content}
            </div>
          ) : (
            <AssistantMessage key={m.id} message={m} />
          )
        )}
        {asking && <Spinner label="El agente está pensando…" />}
        <div ref={endRef} />
      </div>

      <form className="ask-form" onSubmit={submit} noValidate>
        <textarea
          aria-label="Pregunta para el asistente"
          placeholder="Escribe tu pregunta…"
          value={question}
          maxLength={1000}
          rows={2}
          onChange={(e) => setQuestion(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
              e.preventDefault();
              void submit();
            }
          }}
        />
        <div className="row">
          <label className="small">
            <input type="checkbox" checked={useCache} onChange={(e) => setUseCache(e.target.checked)} /> Usar caché
          </label>
          <button type="button" className="btn btn-ghost" onClick={reset} disabled={asking || messages.length === 0}>
            Limpiar
          </button>
          <button type="submit" className="btn btn-primary" disabled={asking}>
            {asking ? 'Consultando…' : 'Preguntar'}
          </button>
        </div>
        {error && <p className="field-error">{error}</p>}
      </form>
    </section>
  );
}
