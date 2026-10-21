# Ejercicio 3 · Desarrollo Front-end — Entregables

| Entregable solicitado | Dónde está |
|---|---|
| Código fuente de la aplicación (React + TypeScript, Vite) | [`frontend/src`](../../frontend/src) · rutas: [`App.tsx`](../../frontend/src/App.tsx) · páginas: [`AuthPages`](../../frontend/src/pages/AuthPages.tsx), [`UsersPages`](../../frontend/src/pages/UsersPages.tsx), [`AdminPages`](../../frontend/src/pages/AdminPages.tsx) (roles y auditoría), [`AssistantPage`](../../frontend/src/pages/AssistantPage.tsx) (IA) |
| Configuración de state management (Zustand) | [`frontend/src/stores`](../../frontend/src/stores): [`authStore`](../../frontend/src/stores/authStore.ts), [`usersStore`](../../frontend/src/stores/usersStore.ts), [`rolesStore`](../../frontend/src/stores/rolesStore.ts), [`auditStore`](../../frontend/src/stores/auditStore.ts), [`aiStore`](../../frontend/src/stores/aiStore.ts), [`toastStore`](../../frontend/src/stores/toastStore.ts) |
| Integración con los microservicios (vía gateway) | Cliente HTTP con refresh automático: [`api/client.ts`](../../frontend/src/api/client.ts) · endpoints tipados: [`api/endpoints.ts`](../../frontend/src/api/endpoints.ts) · proxy nginx → gateway: [`nginx.conf`](../../frontend/nginx.conf) |
| Loading / error / validación / feedback | [`components/common.tsx`](../../frontend/src/components/common.tsx) (Spinner, ErrorBanner con Correlation ID, Toasts, Field) · [`utils/validation.ts`](../../frontend/src/utils/validation.ts) · normalización de errores: [`api/errors.ts`](../../frontend/src/api/errors.ts) |
| Tests (Jest + React Testing Library) | [`frontend/src/__tests__`](../../frontend/src/__tests__): componentes, stores, cliente HTTP y utilidades — **39 tests**, resumen de cobertura: [`coverage-summary.txt`](coverage-summary.txt) |
| Dockerización e inclusión en el compose | [`frontend/Dockerfile`](../../frontend/Dockerfile) (los tests corren dentro del build) · servicio `frontend` en [`docker-compose.yml`](../../docker-compose.yml) |
| Cómo ejecutar el front-end | En el stack: http://localhost:3000 ([GuiaPasoAPaso](../guias/GuiaPasoAPaso.md)) · en desarrollo: `cd frontend; npm ci; npm run dev` (Vite :5173 con proxy al gateway :5000) · tests: `npm test` |

Detalle de decisiones de UX y seguridad del front: [Guía del Implementador §7](../guias/GuiaDelImplementador.md#7-front-end-ejercicio-3).
