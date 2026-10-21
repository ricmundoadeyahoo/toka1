# Sugerencias, riesgos y decisiones a confirmar

Tratando el ejercicio como un requerimiento real, estas son las observaciones que haría al equipo/cliente. Están ordenadas por prioridad: 🔴 atender antes de producción · 🟠 importante · 🟢 mejora.

---

## A. Sobre el documento de requerimientos

| # | Observación | Sugerencia |
|---|---|---|
| A1 🔴 | **Privacidad y cumplimiento con DeepSeek.** Al proveedor viajan la pregunta, los fragmentos de documentos recuperados y las salidas de herramientas (nombres, emails, departamentos de usuarios, eventos de auditoría). DeepSeek procesa los datos en sus propios servidores, fuera de México. Además, el requerimiento lista OpenAI, Azure OpenAI, Anthropic o Gemini; **DeepSeek no está en esa lista** (se integra por su API compatible con OpenAI). | Usar DeepSeek **solo con datos de prueba** y confirmar con el área evaluadora que es aceptable. Para producción: aprobación de Seguridad/Legal (clasificación de datos, residencia, retención del proveedor), o usar Azure OpenAI con residencia en región, o un modelo autohospedado. Cambiar es configuración: `LLM_BASE_URL`, `LLM_API_KEY`, `CHAT_MODEL`. Mitigaciones ya incluidas: salidas de tools compactadas, sin hashes/tokens, redacción de secretos en la respuesta. |
| A1b 🔴 | **La API key de DeepSeek es un secreto con costo asociado.** | Vive solo en `.env` (excluido por `.gitignore`). Antes de cada `git push` verifica con `git status` que `.env` no aparece. Si se filtra, revócala de inmediato en platform.deepseek.com. Define un límite de gasto/recarga baja en la cuenta de pruebas. |
| A2 🟠 | El nombre del archivo dice "Desarrollador **Backend**", el contenido dice "Senior **Full-Stack** Engineer (IA)". | Confirmar el perfil para ajustar el peso de evaluación de front-end. |
| A3 🟠 | La duración estimada (4.25 h) no es realista para el alcance completo (5 microservicios + front + IA + docs + tests ≥70 %). | Acordar alcance mínimo vs. deseable, o asumir que se evalúa diseño + muestra representativa. |
| A4 🟢 | "OAuth2/OIDC distribuida (puede usar JWT con validación distribuida)" es ambiguo sobre si se requiere un IdP completo. | Se implementó un emisor propio con discovery OIDC + JWKS + `/connect/token`. Ver B2 para producción. |
| A5 🟢 | No se especifican requisitos no funcionales medibles (usuarios concurrentes, SLO de latencia, retención de auditoría, presupuesto de IA). | Definirlos; la solución deja todos esos parámetros configurables (retención 365 días, límites de IA, TTL de tokens). |

## B. Plataforma y seguridad

| # | Observación | Sugerencia |
|---|---|---|
| B1 🔴 | **.NET 8 llega a fin de soporte el 10 de noviembre de 2026** (en ~3 semanas a la fecha de este documento). | Planear la migración a **.NET 10 (LTS)**: cambiar `TargetFramework` a `net10.0` en `Directory.Build.props`, imágenes `sdk:10.0`/`aspnet:10.0` y paquetes a 10.x en `Directory.Packages.props`. Se usó .NET 8 porque es lo que pide el requerimiento ("8+") y la base más probada para estas bibliotecas. |
| B2 🔴 | El *grant* `password` (ROPC) está desaconsejado por OAuth 2.0 Security BCP / OAuth 2.1. | En producción usar un IdP (Keycloak, Microsoft Entra ID, Duende IdentityServer) con **Authorization Code + PKCE** para el front y `client_credentials` para servicios. Los servicios no cambian: siguen validando JWT por JWKS. |
| B3 🔴 | Secretos en variables de entorno/archivos (`.env`, clave PEM en volumen). | Azure Key Vault / AWS Secrets Manager / Vault; rotación de la clave de firma publicando varias claves en el JWKS (el validador ya refresca ante `kid` desconocido). |
| B4 🔴 | Tráfico interno sin TLS; Seq y Qdrant sin autenticación en local. | TLS en el borde + mTLS entre servicios (service mesh), `RequireHttpsMetadata=true`, autenticación en Seq/Qdrant/RabbitMQ con usuarios por servicio y *least privilege* por vhost. |
| B5 🟠 | Tokens en `sessionStorage` (expuestos ante XSS). | Patrón **BFF** con cookies `HttpOnly; Secure; SameSite=Strict`; la CSP de nginx ya mitiga XSS. |
| B6 🟠 | El admin semilla tiene contraseña conocida. | Forzar cambio en el primer inicio de sesión y MFA para roles privilegiados. |
| B7 🟠 | No hay flujo de recuperación de contraseña ni verificación de email. | Agregar con tokens de un solo uso y expiración corta. |
| B8 🟢 | Rate limiting en memoria por instancia. | Con varias réplicas, usar rate limiting distribuido (Redis) o en el API Management/Ingress. |

## C. Datos y mensajería

| # | Observación | Sugerencia |
|---|---|---|
| C1 🔴 | *Dual write*: se guarda en BD y luego se publica el evento; si RabbitMQ falla en ese instante, el evento se pierde (se registra el error). | **Transactional Outbox** (tabla outbox en la misma transacción + publicador en segundo plano) o CDC (Debezium). |
| C2 🔴 | El esquema se crea con `EnsureCreated` (no versiona cambios). | Migraciones de EF Core en el pipeline (o ejecutor de migraciones como *job* previo al despliegue). |
| C3 🟠 | Una sola instancia de Postgres para tres bases (aislamiento lógico, no físico). | Instancias/servidores gestionados separados por servicio en producción; PgBouncer para el pool de conexiones. |
| C4 🟠 | Si se requiere SQL Server: | Cambiar `Npgsql.EntityFrameworkCore.PostgreSQL` por `Microsoft.EntityFrameworkCore.SqlServer`, `UseNpgsql`→`UseSqlServer`, mapear `Role.Permissions` (hoy `text[]`) con una *value conversion* a JSON o una tabla hija, y reemplazar `EF.Functions.ILike` por `Like` (SQL Server es *case-insensitive* por collation). |
| C5 🟢 | RabbitMQ.Client 6.x (API síncrona); MongoDB.Driver 2.x. | Migrar a RabbitMQ.Client 7 (API async) y MongoDB.Driver 3 en la próxima iteración; considerar MassTransit si se quieren sagas/reintentos declarativos. |
| C6 🟢 | Auditoría modificable por quien tenga acceso a Mongo. | Para cumplimiento: almacenamiento WORM/inmutable o encadenamiento de hashes por registro. |

## D. Calidad, pruebas y operación

| # | Observación | Sugerencia |
|---|---|---|
| D1 🔴 | El código .NET de este entregable se escribió en un entorno sin SDK de .NET ni acceso a NuGet, así que **su primera compilación y ejecución de tests ocurre en tu equipo** (`docker compose up --build` y `docker compose -f docker-compose.tests.yml run --rm --build tests`). El front-end sí se compiló y sus 39 tests pasaron. | Seguir la [GuiaPasoAPaso](guias/GuiaPasoAPaso.md) (fases de build y tests) y reportar cualquier error de compilación para corregirlo. |
| D2 🟠 | La cobertura se mide sobre Domain + Application (lógica de negocio). | Agregar pruebas de integración con **Testcontainers** (Postgres, Mongo, RabbitMQ, Redis) para Infrastructure/Api y pruebas de contrato (Pact) entre Auth↔Role y AI↔servicios. |
| D3 🟠 | Solo logs; no hay trazas ni métricas técnicas. | **OpenTelemetry** (trazas + métricas) → Jaeger/Tempo + Prometheus/Grafana o Azure Monitor; exportar métricas del agente (latencia, tokens, costo) como métricas, no solo como documentos. |
| D4 🟠 | Sin pipeline CI/CD. | GitHub Actions/Azure DevOps: build, tests, cobertura con umbral, análisis estático (SonarQube), escaneo de dependencias e imágenes (Trivy), firma de imágenes. |
| D5 🟢 | Docker Compose no es un orquestador de producción. | Kubernetes (Helm/Kustomize) con HPA, *readiness/liveness* ya disponibles, *node pool* con GPU para la IA, PodDisruptionBudgets. |
| D6 🟢 | FluentAssertions v8 cambió a licencia comercial. | Por eso los tests usan `Assert` de xUnit; si se desea una librería fluida, usar Shouldly o FluentAssertions 7.x. |

## E. Inteligencia Artificial

| # | Observación | Sugerencia |
|---|---|---|
| E1 🟠 | La validación de calidad es heurística. | Agregar *LLM-as-judge* offline con un modelo mayor y revisión humana por muestreo; versionar resultados por `PromptVersion`. |
| E2 🟠 | Dependencia de un proveedor externo (latencia de red, cuotas, cambios de modelo: DeepSeek ya retiró nombres de modelo anteriores y los redirige). | Fijar el modelo por configuración y revisar los avisos del proveedor; *fallback* a un segundo proveedor (OpenAI) con el mismo adaptador; *router* de intención y *streaming* (SSE) para latencia percibida. |
| E2b 🟠 | Precios de DeepSeek varían por horario (tarifa pico/valle) y por cache hit. | El cálculo usa la tarifa pico (conservador); para conciliación exacta, usar el reporte de facturación del proveedor. |
| E3 🟠 | Caché exacta por texto normalizado. | Caché **semántica** (similitud de embeddings de la pregunta) con umbral alto. |
| E4 🟢 | La base de conocimiento se carga desde archivos de la imagen. | Pipeline de ingesta desde SharePoint/Confluence/Blob con detección de cambios, y metadatos de vigencia/propietario por documento. |
| E5 🟢 | Presupuestos de costo no implementados (solo métricas y límite por minuto). | Presupuesto diario por usuario/área y alertas de costo por hora. |
