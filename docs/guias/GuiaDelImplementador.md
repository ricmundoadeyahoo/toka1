# Guía del Implementador

> Para desarrolladores que mantienen o extienden la solución. Cubre estructura, convenciones, cómo se aplicaron DDD/Clean Architecture en el código, cómo agregar funcionalidades, pruebas, configuración y solución de problemas. La justificación de alto nivel está en [GuiaDeArquitectura.md](../01-arquitectura/GuiaDeArquitectura.md).

---

## 1. Estructura del repositorio

```
toka-user-management/
├── Toka.sln                         # Solución completa (29 proyectos) para Visual Studio / Rider
├── Directory.Build.props            # net8.0, nullable, implicit usings (común a todo)
├── Directory.Packages.props         # Central Package Management: TODAS las versiones NuGet aquí
├── coverage.runsettings             # Alcance de cobertura (Domain + Application + Contracts)
├── docker-compose.yml               # Orquestación completa
├── docker-compose.{openai-embeddings,ollama-native,tests}.yml
├── src/
│   ├── BuildingBlocks/
│   │   ├── Toka.BuildingBlocks.Contracts/       # Puro: Result, IntegrationEvent, IEventPublisher, ICacheService, Permissions, TextInput
│   │   └── Toka.BuildingBlocks.Infrastructure/  # Serilog JSON, JWT distribuido, RabbitMQ, Redis, ProblemDetails, CorrelationId, resiliencia
│   ├── Gateway/Toka.Gateway/                     # YARP
│   └── Services/
│       ├── Auth/   Toka.Auth.{Domain,Application,Infrastructure,Api}   + Dockerfile
│       ├── User/   Toka.User.{Domain,Application,Infrastructure,Api}   + Dockerfile
│       ├── Roles/  Toka.Roles.{Domain,Application,Infrastructure,Api}  + Dockerfile
│       ├── Audit/  Toka.Audit.{Domain,Application,Infrastructure,Api}  + Dockerfile
│       └── Ai/     Toka.Ai.{Domain,Application,Infrastructure,Api}     + Dockerfile
├── tests/
│   ├── Shared/TestDoubles.cs        # FixedTimeProvider, RecordingEventPublisher, InMemoryCacheService (enlazado en todos)
│   ├── Toka.{Auth,User,Roles,Audit,Ai,BuildingBlocks}.UnitTests/
│   └── Dockerfile                   # Corre tests + ReportGenerator sin SDK local
├── frontend/                        # React + TS + Zustand + Jest (+ nginx.conf, Dockerfile)
├── knowledge/                       # Base de conocimiento RAG (*.md) + eval/dataset.json
├── docs/                            # Entregables por ejercicio (01-arquitectura … 05-ia-agentes), guías y sugerencias
├── scripts/                         # smoke-test.ps1, run-tests.ps1 (Windows)
└── docs/                            # Ejercicio 4, prompt engineering, sugerencias
```

> El servicio de roles usa el namespace `Toka.Roles.*` (plural) a propósito: `Toka.Role.*` haría que el nombre `Role` se resolviera al namespace y no a la entidad (error CS0118).

---

## 2. Anatomía de un microservicio (cómo se aplicó Clean Architecture)

Tomando **User Service** como ejemplo:

| Capa | Archivo | Qué contiene | Depende de |
|---|---|---|---|
| Domain | `UserProfile.cs` | Aggregate root con *setters privados*, fábrica `Create`, métodos de comportamiento (`UpdateProfile`, `Deactivate`, `Activate`) que lanzan `DomainException` si se viola una invariante. | nada |
| Application | `Abstractions.cs` | Puerto `IUserRepository`, DTOs (`UserDto`), `Actor` (quién ejecuta). | Domain, Contracts |
| Application | `UserQueries.cs` / `UserCommands.cs` | Casos de uso (CQRS ligero). Validan entrada, aplican autorización a nivel de recurso, invocan el dominio, invalidan caché, publican eventos. Devuelven `Result<T>`. | Domain, Contracts |
| Application | `UserRegisteredHandler.cs` | Manejador del evento `identity.user.registered` (idempotente). | Application |
| Infrastructure | `UserInfrastructure.cs` | `UserDbContext` (mapeo EF Core fluido, sin atributos en el dominio), `UserRepository`, registro de DI, consumidor RabbitMQ, *seed*. | Application, BuildingBlocks.Infrastructure |
| Api | `Program.cs` | Composition root + endpoints Minimal API + políticas por permiso. Traduce `Result` → HTTP con `ToHttpResult()`. | Infrastructure |

**Principios SOLID visibles en el código**

- **S**: cada caso de uso es una clase pequeña con una razón para cambiar (`LoginUseCase`, `RefreshTokenUseCase`, `LogoutUseCase`…).
- **O**: agregar un consumidor de eventos no modifica productores; agregar una herramienta al agente es una entrada en `PromptLibrary.Tools` + un `case` en el ejecutor.
- **L/I**: puertos chicos y específicos (`IRefreshTokenStore`, `ITokenRevocationStore` en vez de un "IRedisService" genérico).
- **D**: Application depende de abstracciones (`ILlmClient`, `IVectorStore`, `IEventPublisher`); las implementaciones se inyectan en el composition root.

---

## 3. Piezas transversales (BuildingBlocks)

| Pieza | Uso |
|---|---|
| `builder.AddTokaPlatform("svc")` / `app.UseTokaPlatform()` | "Chasis" común: Serilog JSON + Seq, ProblemDetails, `GlobalExceptionHandler`, CorrelationId, cabeceras de seguridad, JWT, Redis, RabbitMQ, Swagger (Development), `/health/live` y `/health/ready`. |
| `Result` / `Error` / `ErrorType` | Errores de negocio. `ToHttpResult()` mapea Validation→400, NotFound→404, Conflict→409, Unauthorized→401, Forbidden→403, Unavailable→503, RateLimited→429. |
| `IEventPublisher` + `IntegrationEvent` + `EventTypes` | Publicación de eventos. El publicador rellena `CorrelationId` desde el contexto. |
| `services.AddEventConsumer<THandler>("cola")` | Declara cola durable + DLQ y la enlaza a `handler.EventTypes`. |
| `ICacheService` + `CacheKeys` | Cache-aside tolerante a fallos. |
| `.AddTokaResilience()` sobre un `IHttpClientBuilder` | Retry + circuit breaker + timeouts + propagación de Correlation ID. |
| `RequireAuthorization(Permissions.X)` | Una política por permiso, generada automáticamente. |
| `TextInput.Validate/Sanitize/IsSafe` | Validación OWASP de texto libre. |

### 3.1 Logging estructurado

- Formato **CLEF** (JSON compacto) por consola → `docker compose logs <svc>` y **Seq** (`http://localhost:5341`).
- Propiedades presentes en cada evento: `service`, `environment`, `CorrelationId`; en request logs además `RequestPath`, `StatusCode`, `Elapsed`, `UserId`, `ClientIp`.
- Usar **plantillas** con propiedades (`logger.LogInformation("Perfil creado para {UserId}", id)`), nunca interpolación: así se puede filtrar en Seq (`UserId = '...'`).
- **Nunca** registrar contraseñas, tokens, hashes ni cuerpos de petición.

Consultas útiles en Seq:

```
CorrelationId = 'smoke-1a2b3c'                       -- seguir una petición entre servicios
@Level = 'Error' and service = 'auth-service'
StatusCode >= 500
@MessageTemplate like 'Consulta IA%' and LatencyMs > 20000
```

---

## 4. Autenticación OAuth2/OIDC distribuida

- **Emisor**: Auth Service firma con RSA 2048 (RS256). La clave se genera al primer arranque y se guarda en el volumen `auth-keys` (`/keys/signing-key.pem`).
- **Endpoints**: `/.well-known/openid-configuration`, `/.well-known/jwks.json`, `/connect/token` (`grant_type=password|refresh_token|client_credentials`, form-urlencoded) y la API JSON `/api/auth/{register,login,refresh,logout,me}`.
- **Validadores**: el resto de servicios configura `JwtBearer` con `MetadataAddress` apuntando a Auth; validan firma/iss/aud/exp localmente y revisan revocación en Redis (`auth:revoked:{jti}`).
- **Claims**: `sub`, `email`, `role` (múltiple), `permission` (múltiple), `scope`, `client_id`, `jti`.
- **Servicio a servicio**: Auth se autentica ante Role Service con un token propio (scope `roles.read`). Para otros clientes confidenciales se registran en configuración:

```text
OAuthClients__0__ClientId=reporting-job
OAuthClients__0__ClientSecret=<mínimo 16 caracteres>
OAuthClients__0__AllowedScopes__0=users.read
```

Ejemplo `client_credentials`:

```powershell
Invoke-RestMethod -Method POST http://localhost:5000/connect/token `
  -ContentType "application/x-www-form-urlencoded" `
  -Body "grant_type=client_credentials&client_id=reporting-job&client_secret=...&scope=users.read"
```

---

## 5. Cómo extender

### 5.1 Agregar un endpoint/caso de uso

1. Si hay una nueva regla de negocio, impleméntala en la **entidad** (método + `DomainException`).
2. Crea/extiende el caso de uso en Application devolviendo `Result<T>`. Valida entradas con `TextInput` y autoriza a nivel de recurso si aplica.
3. Publica un evento si el cambio es auditable (`EventTypes` + `IEventPublisher`).
4. Expón el endpoint en `Program.cs` con `.RequireAuthorization(Permissions.X)` y `.ToHttpResult()`.
5. Agrega tests del dominio y del caso de uso (usa los dobles de `tests/Shared`).

### 5.2 Agregar un evento y un consumidor

```csharp
// 1) Contracts/Events/EventTypes.cs
public const string UserPasswordChanged = "identity.password.changed";

// 2) Application del consumidor
public sealed class PasswordChangedHandler(...) : IIntegrationEventHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } = [global::Toka.BuildingBlocks.Contracts.Events.EventTypes.UserPasswordChanged];
    public Task HandleAsync(IntegrationEvent e, CancellationToken ct) { /* idempotente */ }
}

// 3) Infrastructure del consumidor
services.AddEventConsumer<PasswordChangedHandler>("mi-servicio.password-changes");
```

Audit lo registrará automáticamente (escucha `#`).

### 5.3 Agregar un permiso o rol

- Permiso: constante en `Permissions` + agregarlo a `Permissions.All` (crea la política automáticamente) + incluirlo en el seed de `RolesInfrastructure.cs` si un rol de sistema debe tenerlo.
- Rol personalizado: por API (`POST /api/roles`) sin cambios de código.

### 5.4 Agregar una herramienta al agente de IA

1. `PromptLibrary.Tools`: nombre, descripción clara (el modelo decide con ella) y JSON Schema de parámetros.
2. `ServiceToolExecutor.ExecuteAsync`: nuevo `case`, llamando al servicio con el token del usuario y **compactando** la respuesta (solo campos necesarios → menos tokens).
3. Si cambia el comportamiento, **incrementa `PromptLibrary.PromptVersion`** (invalida caché y permite comparar métricas por versión).
4. Agrega un caso al dataset `knowledge/eval/dataset.json`.

### 5.5 Agregar conocimiento al RAG

- Archivo `.md` en `knowledge/` (se copia a la imagen) y `POST /api/ai/knowledge/reindex`, **o**
- En caliente: `POST /api/ai/knowledge/documents {title, content}` (requiere `ai:admin`).

### 5.6 Proveedor de IA (DeepSeek por defecto) y cómo cambiarlo

El AI Service tiene **dos endpoints OpenAI-compatible independientes** (`Llm:Chat` y `Llm:Embeddings`), implementados en
[`OpenAiCompatibleClient.cs`](../../src/Services/Ai/Toka.Ai.Infrastructure/Llm/OpenAiCompatibleClient.cs) (`OpenAiChatClient`, `OpenAiEmbeddingClient`).

| Uso | Default | Por qué |
|---|---|---|
| Chat + function calling | DeepSeek `deepseek-flash` (`https://api.deepseek.com`) | Bajo costo por token, soporta tools, API OpenAI-compatible. |
| Embeddings del RAG | Ollama local `nomic-embed-text` | **DeepSeek no ofrece API de embeddings**; el modelo local pesa ~270 MB y los documentos no salen de la red para vectorizarse. |

Detalles específicos de DeepSeek que el adaptador ya maneja:

- **Modo thinking activo por defecto** en la API: si el host es `*.deepseek.com` y `ExtraBodyJson` está vacío, se envía `{"thinking":{"type":"disabled"}}` (menos latencia y sin tokens de razonamiento). `LLM_EXTRA_BODY_JSON=none` lo desactiva; cualquier JSON propio se fusiona en el cuerpo de la petición.
- **Caché de contexto del proveedor**: se lee `usage.prompt_cache_hit_tokens` (DeepSeek) o `usage.prompt_tokens_details.cached_tokens` (OpenAI) y se cobra con `CachedInputPerMillionUsd`.
- **Errores**: 429 → `LlmRateLimitedException` (HTTP 429 al cliente, respetando `Retry-After` en los reintentos); 401/402/403 → mensaje "revisa API key y saldo"; sin key configurada → 503 inmediato sin llamar al proveedor.
- Con tools y content vacío, el mensaje de asistente se envía con `content: null`.

Cambiar de proveedor (en `.env`):

```text
# OpenAI para el chat
LLM_BASE_URL=https://api.openai.com/v1
LLM_API_KEY=sk-...
CHAT_MODEL=gpt-4o-mini
PRICE_INPUT_PER_M=0.15
PRICE_CACHED_INPUT_PER_M=0.075
PRICE_OUTPUT_PER_M=0.60

# DeepSeek modelo Pro
CHAT_MODEL=deepseek-v4-pro
PRICE_INPUT_PER_M=1.32
PRICE_CACHED_INPUT_PER_M=0.044
PRICE_OUTPUT_PER_M=3.96
```

Embeddings con OpenAI: `docker compose -f docker-compose.yml -f docker-compose.openai-embeddings.yml up -d` (requiere `OPENAI_API_KEY`). Al cambiar el modelo de embeddings cambia la dimensión: Qdrant detecta la diferencia, **recrea la colección** y conviene ejecutar `POST /api/ai/knowledge/reindex`. Los precios son de la documentación oficial de DeepSeek a la fecha (tarifa pico; la valle es la mitad): verifícalos antes de presupuestar.

---

## 6. Pruebas

### 6.1 Backend (.NET)

- **xUnit + Moq**, un proyecto por servicio + BuildingBlocks: **~234 casos** (contando cada `InlineData` de los `Theory`).
- Dobles compartidos en `tests/Shared/TestDoubles.cs` (reloj fijo, publicador que registra eventos, caché en memoria con serialización JSON real).
- **Alcance de cobertura** (`coverage.runsettings`): `Domain`, `Application` y `Contracts`, que es donde vive la lógica; se excluye código generado y registro de DI. Infrastructure/Api se validan con la prueba de humo end-to-end (`scripts/smoke-test.ps1`) y se proponen pruebas de integración con Testcontainers (ver Sugerencias).
- Sin SDK local (recomendado en Windows):

```powershell
docker compose -f docker-compose.tests.yml run --rm --build tests
# Reporte: .\coverage\report\index.html   ·   resumen: .\coverage\report\Summary.txt
```

- Con SDK .NET 8 instalado:

```powershell
dotnet test Toka.sln --settings coverage.runsettings --collect:"XPlat Code Coverage"
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"tests/**/coverage.cobertura.xml" -targetdir:coverage/report -reporttypes:"Html;TextSummary"
```

### 6.2 Front-end

```powershell
cd frontend
npm ci
npm test               # Jest + React Testing Library
npm run test:coverage  # reporte en frontend/coverage
```

Cubre validaciones, normalización de errores, interceptor de refresh token, stores (Zustand) y componentes (login, registro, rutas protegidas, tabla de usuarios con loading/error/vacío, asistente IA con métricas).

---

## 7. Front-end (Ejercicio 3)

| Tema | Implementación |
|---|---|
| State management | **Zustand**: `authStore` (persistido en `sessionStorage`), `usersStore`, `rolesStore`, `auditStore`, `aiStore`, `toastStore`. Cada store expone `loading`, `error` y acciones async. |
| Integración | `api/client.ts` (axios): agrega el bearer, ante **401** intenta **un** refresh (deduplicado) y reintenta; `api/endpoints.ts` tipado contra los DTOs del backend. Todo pasa por el gateway en el mismo origen (`/api` → nginx → gateway) ⇒ sin CORS en el navegador. |
| UX | Spinners/estados de carga, banners de error con **Correlation ID** y botón *Reintentar*, estados vacíos, toasts de éxito/error, validación por campo con `aria-invalid`/`aria-describedby`. |
| Seguridad | Rutas protegidas por permiso (la UI oculta lo que el token no permite; el backend lo vuelve a validar), CSP y cabeceras en nginx, validación que rechaza `<`/`>`. |
| Pantallas | Login, Registro, Mi perfil (editar), Usuarios (búsqueda, filtro, paginación, edición en línea, activar/desactivar), Roles (catálogo y asignación/revocación), Auditoría (filtros), Asistente IA (fuentes, latencia, tokens, costo real y de referencia, calidad, KPIs agregados para `ai:admin`). |
| Desarrollo local | `npm run dev` (Vite en :5173 con proxy a `localhost:5000`). |

---

## 8. Configuración (variables de entorno principales)

| Variable | Servicio | Default | Descripción |
|---|---|---|---|
| `ConnectionStrings__{AuthDb,UsersDb,RolesDb}` | auth/user/role | Postgres local | Base de cada servicio |
| `ConnectionStrings__Redis` | todos | `redis:6379` | Caché / revocación |
| `RabbitMq__{Host,UserName,Password}` | todos | `rabbitmq` / `toka` | Broker |
| `Jwt__{Issuer,Audience,Authority}` | todos | `http://auth-service:8080`, `toka-api` | Validación distribuida |
| `Auth__{AccessTokenMinutes,RefreshTokenDays,MaxFailedAttempts,LockoutMinutes}` | auth | 15, 7, 5, 15 | Política de sesión |
| `Seed__AdminPassword` | auth | `Admin123!` | Solo en el primer arranque |
| `Llm__Chat__{BaseUrl,ApiKey,Model,ExtraBodyJson}` | ai | DeepSeek `deepseek-flash` | Chat + tools (`.env`: `LLM_BASE_URL`, `LLM_API_KEY`, `CHAT_MODEL`, `LLM_EXTRA_BODY_JSON`) |
| `Llm__Embeddings__{BaseUrl,ApiKey,Model}` | ai | Ollama local `nomic-embed-text` | Embeddings del RAG (`EMBEDDINGS_*`, `EMBEDDING_MODEL`) |
| `Llm__{MaxConcurrency,QueueTimeoutSeconds,AttemptTimeoutSeconds}` | ai | 4, 30, 60 | Concurrencia y timeouts hacia el proveedor |
| `Agent__{TopK,MinRelevanceScore,MaxContextTokens,MaxToolIterations,CacheMinutes,RequestsPerMinutePerUser}` | ai | 4, 0.45, 1500, 3, 60, 10 | Afinación del agente |
| `Agent__Pricing__*`, `Agent__ReferencePricing__*` | ai | 0 / precios de referencia | Cálculo de costos |
| `Logging__SeqUrl` | todos | `http://seq:5341` | Log centralizado |

---

## 9. Persistencia y migraciones

- El esquema se crea con `EnsureCreated` al arrancar (con reintentos) y se siembran: roles de sistema (`Admin`, `Auditor`, `User`), el administrador `admin@toka.local` (mismo GUID en Auth, User y Role) y su asignación.
- **Para producción** usar migraciones de EF Core (ver Sugerencias):

```powershell
dotnet tool install -g dotnet-ef
dotnet ef migrations add Initial -p src/Services/User/Toka.User.Infrastructure -s src/Services/User/Toka.User.Api
```

y reemplazar `EnsureCreatedAsync()` por `MigrateAsync()`.

---

## 10. Solución de problemas

| Síntoma | Causa probable | Acción |
|---|---|---|
| El asistente responde 503 "No hay API key configurada" | `LLM_API_KEY` vacía o con el placeholder | Edita `.env` y ejecuta `docker compose up -d ai-service`. |
| 503 "rechazó la petición (401/402)" | Key inválida o **sin saldo** en DeepSeek | Revisa la key y el saldo en platform.deepseek.com. |
| 429 frecuentes | Límite del proveedor o `RequestsPerMinutePerUser` | Espera; baja `Llm__MaxConcurrency`; revisa la cuota de tu cuenta. |
| 503 sin respuesta del proveedor | Sin salida a internet desde Docker (proxy corporativo) | Configura el proxy en Docker Desktop → *Resources → Proxies*. |
| La base de conocimiento tiene 0 chunks | Indexación en reintento | `docker compose logs ai-service` (busca "Indexación inicial"); `POST /api/ai/knowledge/reindex`. |
| 401 en todos los servicios tras recrear volúmenes | Clave de firma regenerada; tokens viejos | Cerrar sesión e ingresar de nuevo. |
| `bind: address already in use` | Puerto ocupado en Windows | Cambiar `*_PORT` en `.env`. |
| Login 503 `auth.roles_unavailable` | Role Service caído y sin caché previa | `docker compose ps role-service`; revisar logs. |
| Mensajes en `*.dlq` | Handler falló o mensaje mal formado | RabbitMQ UI → Queues → `*.dlq` → *Get messages*; corregir y re-publicar. |
| La base de conocimiento queda en 0 chunks | El modelo de embeddings aún se descarga | `docker compose logs -f ollama-init` (espera "Modelo de embeddings listo"); el AI Service reintenta solo. |
