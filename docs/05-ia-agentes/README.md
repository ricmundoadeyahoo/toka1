# Ejercicio 5 · Integración de IA y Agentes — Entregables

**Proveedor:** chat y *function calling* con **DeepSeek** (`deepseek-flash`, API compatible con OpenAI) · embeddings con `nomic-embed-text` local (DeepSeek no ofrece embeddings) · vector DB **Qdrant**.

| Entregable solicitado | Dónde está |
|---|---|
| Código del microservicio de IA | [`src/Services/Ai`](../../src/Services/Ai) — endpoints: [`Program.cs`](../../src/Services/Ai/Toka.Ai.Api/Program.cs) · agente: [`AskAgentUseCase.cs`](../../src/Services/Ai/Toka.Ai.Application/UseCases/AskAgentUseCase.cs) · adaptador OpenAI-compatible (DeepSeek): [`OpenAiCompatibleClient.cs`](../../src/Services/Ai/Toka.Ai.Infrastructure/Llm/OpenAiCompatibleClient.cs) |
| Pipeline de embeddings | Chunking: [`TextChunker.cs`](../../src/Services/Ai/Toka.Ai.Application/Rag/TextChunker.cs) → embeddings + upsert: [`IngestKnowledgeUseCase.cs`](../../src/Services/Ai/Toka.Ai.Application/UseCases/IngestKnowledgeUseCase.cs) → vector DB: [`QdrantVectorStore.cs`](../../src/Services/Ai/Toka.Ai.Infrastructure/VectorStore/QdrantVectorStore.cs) · indexación al arrancar: [`FileKnowledgeSource.cs`](../../src/Services/Ai/Toka.Ai.Infrastructure/Knowledge/FileKnowledgeSource.cs) · base de conocimiento: [`knowledge/`](../../knowledge) |
| Estrategia de prompt engineering | [PromptEngineering.md](PromptEngineering.md) · prompts versionados: [`PromptLibrary.cs`](../../src/Services/Ai/Toka.Ai.Application/Prompts/PromptLibrary.cs) |
| Evaluación: latencia, costo de tokens, calidad | Métricas por consulta y costo: [`AiModel.cs`](../../src/Services/Ai/Toka.Ai.Domain/AiModel.cs) (`TokenUsage`, `ModelPricing` con cache hit, `QueryMetrics`) · calidad: [`ResponseValidator.cs`](../../src/Services/Ai/Toka.Ai.Application/Evaluation/ResponseValidator.cs) · agregados p50/p95: [`MetricsSummary.cs`](../../src/Services/Ai/Toka.Ai.Application/Evaluation/MetricsSummary.cs) · evaluación offline: [`EvaluateAgentUseCase.cs`](../../src/Services/Ai/Toka.Ai.Application/UseCases/EvaluateAgentUseCase.cs) + [`knowledge/eval/dataset.json`](../../knowledge/eval/dataset.json) |
| Ejemplo de uso integrado con los microservicios | Herramientas con *token relay* sobre User/Role/Audit: [`ServiceToolExecutor.cs`](../../src/Services/Ai/Toka.Ai.Infrastructure/Tools/ServiceToolExecutor.cs) · ejemplos ejecutables: fase "Agente de IA" de la [GuiaPasoAPaso](../guias/GuiaPasoAPaso.md) y [`scripts/smoke-test.ps1`](../../scripts/smoke-test.ps1) · UI: [`AssistantPage.tsx`](../../frontend/src/pages/AssistantPage.tsx) |
| Explicación de la integración con el sistema | [Arquitectura §4.4 y §10](../01-arquitectura/GuiaDeArquitectura.md#10-integración-de-ia-en-la-arquitectura) |
| Manejo de errores y rate limiting de APIs de IA | [PromptEngineering §6](PromptEngineering.md#6-manejo-de-errores-y-límites-del-proveedor) · tests: [`LlmClientTests.cs`](../../tests/Toka.Ai.UnitTests/LlmClientTests.cs), [`AgentTests.cs`](../../tests/Toka.Ai.UnitTests/AgentTests.cs) |

## Endpoints del AI Service (vía gateway `http://localhost:5000`)

| Método y ruta | Permiso | Uso |
|---|---|---|
| `POST /api/ai/ask` `{question, useCache}` | `ai:use` | Pregunta al agente: respuesta + fuentes + herramientas usadas + métricas (latencia, tokens, cache hit, costo) + calidad |
| `GET /api/ai/knowledge/status` | `ai:use` | Chunks indexados, modelo y proveedor de chat y de embeddings |
| `POST /api/ai/knowledge/reindex` | `ai:admin` | Reindexa `knowledge/*.md` |
| `POST /api/ai/knowledge/documents` `{title, content}` | `ai:admin` | Agrega un documento en caliente |
| `GET /api/ai/metrics/summary` · `/metrics/recent` | `ai:admin` | Agregados (p50/p95, tokens, costo, cache hit, calidad) y últimas consultas |
| `POST /api/ai/evaluate?maxCases=8` | `ai:admin` | Evaluación contra el golden dataset |
