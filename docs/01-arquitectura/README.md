# Ejercicio 1 · Diseño y Arquitectura de Software — Entregables

| Entregable solicitado | Dónde está |
|---|---|
| Diagrama(s) de arquitectura (visual o Mermaid) con todos los microservicios e interacciones | [GuiaDeArquitectura.md §2 Contexto](GuiaDeArquitectura.md#2-vista-de-contexto) · [§3 Contenedores](GuiaDeArquitectura.md#3-vista-de-contenedores-microservicios-e-interacciones) · [§7.1 Capas](GuiaDeArquitectura.md#71-capas-por-microservicio) |
| Justificación de decisiones técnicas | [§5 Comunicación](GuiaDeArquitectura.md#5-comunicación-entre-microservicios) · [§6 Datos](GuiaDeArquitectura.md#6-estrategia-de-datos-multi-db-database-per-service) · [§12 ADRs](GuiaDeArquitectura.md#12-registro-de-decisiones-adr-resumidos) |
| Flujo de datos entre microservicios | [§4 Flujos (registro, login, petición autenticada, agente IA)](GuiaDeArquitectura.md#4-flujos-de-datos-entre-microservicios) |
| Diseño de `docker-compose.yml` | [§11 Estructura](GuiaDeArquitectura.md#11-diseño-de-docker-composeyml-estructura) · archivo real: [`docker-compose.yml`](../../docker-compose.yml) |
| Aplicación de DDD y Clean Architecture | [§7 DDD y Clean Architecture](GuiaDeArquitectura.md#7-ddd-y-clean-architecture) · detalle en código: [Guía del Implementador §2](../guias/GuiaDelImplementador.md#2-anatomía-de-un-microservicio-cómo-se-aplicó-clean-architecture) |

Criterios de evaluación cubiertos: seguridad ([§8](GuiaDeArquitectura.md#8-seguridad-owasp--zero-trust)), escalabilidad y resiliencia ([§9](GuiaDeArquitectura.md#9-escalabilidad-y-resiliencia)), integración de IA ([§10](GuiaDeArquitectura.md#10-integración-de-ia-en-la-arquitectura)).
