# Ejercicio 2 · Implementación de Microservicios — Entregables

## Código fuente

| Microservicio | Capas (Clean Architecture) | Dockerfile | Persistencia |
|---|---|---|---|
| Auth Service | [Domain](../../src/Services/Auth/Toka.Auth.Domain) · [Application](../../src/Services/Auth/Toka.Auth.Application) · [Infrastructure](../../src/Services/Auth/Toka.Auth.Infrastructure) · [Api](../../src/Services/Auth/Toka.Auth.Api) | [Dockerfile](../../src/Services/Auth/Dockerfile) | Postgres `auth_db`, Redis |
| User Service | [Domain](../../src/Services/User/Toka.User.Domain) · [Application](../../src/Services/User/Toka.User.Application) · [Infrastructure](../../src/Services/User/Toka.User.Infrastructure) · [Api](../../src/Services/User/Toka.User.Api) | [Dockerfile](../../src/Services/User/Dockerfile) | Postgres `users_db`, Redis |
| Role Service | [Domain](../../src/Services/Roles/Toka.Roles.Domain) · [Application](../../src/Services/Roles/Toka.Roles.Application) · [Infrastructure](../../src/Services/Roles/Toka.Roles.Infrastructure) · [Api](../../src/Services/Roles/Toka.Roles.Api) | [Dockerfile](../../src/Services/Roles/Dockerfile) | Postgres `roles_db`, Redis |
| Audit Service | [Domain](../../src/Services/Audit/Toka.Audit.Domain) · [Application](../../src/Services/Audit/Toka.Audit.Application) · [Infrastructure](../../src/Services/Audit/Toka.Audit.Infrastructure) · [Api](../../src/Services/Audit/Toka.Audit.Api) | [Dockerfile](../../src/Services/Audit/Dockerfile) | MongoDB `audit_db` |
| AI Service (ver [Ejercicio 5](../05-ia-agentes/README.md)) | [Domain](../../src/Services/Ai/Toka.Ai.Domain) · [Application](../../src/Services/Ai/Toka.Ai.Application) · [Infrastructure](../../src/Services/Ai/Toka.Ai.Infrastructure) · [Api](../../src/Services/Ai/Toka.Ai.Api) | [Dockerfile](../../src/Services/Ai/Dockerfile) | Qdrant, MongoDB `ai_db`, Redis |
| API Gateway (YARP) | [Toka.Gateway](../../src/Gateway/Toka.Gateway) | [Dockerfile](../../src/Gateway/Dockerfile) | — |
| Piezas transversales | [Contracts](../../src/BuildingBlocks/Toka.BuildingBlocks.Contracts) · [Infrastructure](../../src/BuildingBlocks/Toka.BuildingBlocks.Infrastructure) | — | — |

Solución para Visual Studio/Rider: [`Toka.sln`](../../Toka.sln) · versiones NuGet centralizadas: [`Directory.Packages.props`](../../Directory.Packages.props).

## Requisitos técnicos → implementación

| Requisito | Implementación |
|---|---|
| REST síncrono entre servicios | Auth → Role: [`RoleServiceClient.cs`](../../src/Services/Auth/Toka.Auth.Infrastructure/Clients/RoleServiceClient.cs) (retry + circuit breaker + fallback en Redis) |
| Comunicación asíncrona (RabbitMQ) | Publicador [`RabbitMqEventPublisher.cs`](../../src/BuildingBlocks/Toka.BuildingBlocks.Infrastructure/Messaging/RabbitMqEventPublisher.cs) · consumidor con DLQ [`RabbitMqConsumerService.cs`](../../src/BuildingBlocks/Toka.BuildingBlocks.Infrastructure/Messaging/RabbitMqConsumerService.cs) · catálogo [`EventTypes.cs`](../../src/BuildingBlocks/Toka.BuildingBlocks.Contracts/Events/EventTypes.cs) · handlers: [`UserRegisteredHandler`](../../src/Services/User/Toka.User.Application/UserRegisteredHandler.cs), [`AssignDefaultRoleHandler`](../../src/Services/Roles/Toka.Roles.Application/RoleUseCases.cs), [`UserStatusChangedHandler`](../../src/Services/Auth/Toka.Auth.Application/EventHandlers/UserStatusChangedHandler.cs), [`AuditEventHandler`](../../src/Services/Audit/Toka.Audit.Application/AuditUseCases.cs) |
| OAuth2/OIDC distribuido (JWT) | Emisor: [`JwtTokenService.cs`](../../src/Services/Auth/Toka.Auth.Infrastructure/Security/JwtTokenService.cs), [`RsaKeyProvider.cs`](../../src/Services/Auth/Toka.Auth.Infrastructure/Security/RsaKeyProvider.cs), endpoints OIDC/JWKS/token: [`AuthEndpoints.cs`](../../src/Services/Auth/Toka.Auth.Api/Endpoints/AuthEndpoints.cs) · Validación distribuida: [`JwtAuthenticationExtensions.cs`](../../src/BuildingBlocks/Toka.BuildingBlocks.Infrastructure/Security/JwtAuthenticationExtensions.cs) |
| DDD + Clean Architecture | Agregados con invariantes, p. ej. [`UserCredential.cs`](../../src/Services/Auth/Toka.Auth.Domain/UserCredential.cs), [`Role.cs`](../../src/Services/Roles/Toka.Roles.Domain/Role.cs); casos de uso con `Result<T>`; explicación en [Guía del Implementador §2](../guias/GuiaDelImplementador.md#2-anatomía-de-un-microservicio-cómo-se-aplicó-clean-architecture) y [Arquitectura §7](../01-arquitectura/GuiaDeArquitectura.md#7-ddd-y-clean-architecture) |
| ORM / multi-DB | EF Core + Npgsql ([`AuthDbContext`](../../src/Services/Auth/Toka.Auth.Infrastructure/Persistence/AuthDbContext.cs), [`UserInfrastructure`](../../src/Services/User/Toka.User.Infrastructure/UserInfrastructure.cs), [`RolesInfrastructure`](../../src/Services/Roles/Toka.Roles.Infrastructure/RolesInfrastructure.cs)) · MongoDB ([`MongoAuditRepository`](../../src/Services/Audit/Toka.Audit.Infrastructure/MongoAuditRepository.cs)) · Redis ([`DistributedCacheService`](../../src/BuildingBlocks/Toka.BuildingBlocks.Infrastructure/Caching/DistributedCacheService.cs)) |
| Logging estructurado JSON | [`SerilogExtensions.cs`](../../src/BuildingBlocks/Toka.BuildingBlocks.Infrastructure/Logging/SerilogExtensions.cs) + [`CorrelationIdMiddleware.cs`](../../src/BuildingBlocks/Toka.BuildingBlocks.Infrastructure/Http/CorrelationIdMiddleware.cs) → consola JSON + Seq |
| OWASP (validación, sanitización) | [`TextInput.cs`](../../src/BuildingBlocks/Toka.BuildingBlocks.Contracts/Validation/TextInput.cs), [`ValueObjects.cs`](../../src/Services/Auth/Toka.Auth.Domain/ValueObjects.cs) (política de contraseñas), [`LoginUseCase.cs`](../../src/Services/Auth/Toka.Auth.Application/UseCases/LoginUseCase.cs) (lockout, anti-enumeración), [`GlobalExceptionHandler.cs`](../../src/BuildingBlocks/Toka.BuildingBlocks.Infrastructure/Http/GlobalExceptionHandler.cs) |
| Dockerización | Un Dockerfile por servicio (tabla de arriba) + [`docker-compose.yml`](../../docker-compose.yml) |

## Tests unitarios y reporte de cobertura

- Proyectos: [`tests/`](../../tests) (xUnit + Moq; dobles compartidos en [`tests/Shared`](../../tests/Shared/TestDoubles.cs)).
- Alcance de cobertura (Domain + Application + Contracts): [`coverage.runsettings`](../../coverage.runsettings).
- Ejecución sin SDK local: [`docker-compose.tests.yml`](../../docker-compose.tests.yml) / [`scripts/run-tests.ps1`](../../scripts/run-tests.ps1).
- **Reporte**: [`coverage/`](coverage/README.md) — `run-tests.ps1` copia aquí el resumen (`Summary.md`, `Summary.txt`) para versionarlo en GitHub; el HTML completo queda en `coverage/report/index.html` (raíz, ignorado por git).

## Documentación de ejecución

- Paso a paso en Windows: [GuiaPasoAPaso.md](../guias/GuiaPasoAPaso.md)
- Configuración, extensión y troubleshooting: [GuiaDelImplementador.md](../guias/GuiaDelImplementador.md)
