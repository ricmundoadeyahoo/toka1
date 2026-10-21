using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Toka.Ai.Application.Evaluation;
using Toka.Ai.Application.Prompts;
using Toka.Ai.Application.Rag;
using Toka.Ai.Domain;
using Toka.BuildingBlocks.Contracts.Caching;
using Toka.BuildingBlocks.Contracts.Events;
using Toka.BuildingBlocks.Contracts.Results;
using Toka.BuildingBlocks.Contracts.Validation;

namespace Toka.Ai.Application.UseCases;

public sealed record AskCommand(string Question, bool UseCache, AgentContext Context);

public sealed record SourceDto(int Index, string Title, string Source, double Score);

public sealed record AnswerMetricsDto(
    long LatencyMs, long RetrievalMs, long LlmMs, long ToolMs, int LlmCalls,
    int PromptTokens, int CachedPromptTokens, int CompletionTokens, int EmbeddingTokens, int TotalTokens,
    decimal EstimatedCostUsd, decimal ReferenceCostUsd, string ReferenceModel, string Model, string PromptVersion, bool CacheHit);

public sealed record AskResponse(
    Guid QueryId, string Answer, IReadOnlyList<SourceDto> Sources, IReadOnlyList<string> ToolsUsed,
    AnswerMetricsDto Metrics, QualityReport Quality, bool PossibleInjection);

/// <summary>
/// Agente RAG con herramientas (ReAct simplificado):
/// 1) valida y sanitiza la pregunta, 2) consulta caché, 3) recupera contexto (embeddings + Qdrant),
/// 4) llama al LLM con system prompt + few-shot + contexto + tools, 5) ejecuta tools contra los microservicios
/// (máx. N iteraciones), 6) valida la respuesta, 7) mide latencia/tokens/costo, 8) persiste métricas y publica evento.
/// </summary>
public sealed class AskAgentUseCase(
    ILlmClient llm,
    IEmbeddingClient embeddings,
    IVectorStore vectorStore,
    IAgentToolExecutor toolExecutor,
    IMetricsRepository metricsRepository,
    ICacheService cache,
    IEventPublisher eventPublisher,
    IOptions<AgentOptions> options,
    TimeProvider timeProvider,
    ILogger<AskAgentUseCase> logger)
{
    internal sealed record CachedAnswer(string Answer, List<SourceDto> Sources, QualityReport Quality);

    public async Task<Result<AskResponse>> ExecuteAsync(AskCommand command, CancellationToken ct)
    {
        var opts = options.Value;
        var question = TextInput.Sanitize(command.Question, opts.MaxQuestionLength + 1);
        if (question.Length < 3) return Error.Validation("ai.question_short", "La pregunta debe tener al menos 3 caracteres.");
        if (question.Length > opts.MaxQuestionLength)
            return Error.Validation("ai.question_long", $"La pregunta excede {opts.MaxQuestionLength} caracteres.");

        var possibleInjection = InputGuard.LooksLikeInjection(question);
        if (possibleInjection) logger.LogWarning("Posible prompt injection de {UserId}", command.Context.UserId);

        var total = Stopwatch.StartNew();
        var cacheKey = CacheKey(question);

        // ---- 1. Caché semántica exacta (pregunta normalizada) → 0 tokens ----
        if (command.UseCache && !possibleInjection)
        {
            var cached = await cache.GetAsync<CachedAnswer>(cacheKey, ct);
            if (cached is not null)
            {
                var hitMetrics = new QueryMetrics
                {
                    Timestamp = timeProvider.GetUtcNow(), UserId = command.Context.UserId, QuestionPreview = Preview(question),
                    Model = llm.ChatModel, PromptVersion = PromptLibrary.PromptVersion, TotalLatencyMs = total.ElapsedMilliseconds,
                    QualityScore = cached.Quality.Score, QualityPassed = cached.Quality.Passed, CacheHit = true, Success = true,
                    ChunksRetrieved = cached.Sources.Count
                };
                await SaveMetricsAsync(hitMetrics, ct);
                return Result<AskResponse>.Success(new AskResponse(hitMetrics.Id, cached.Answer, cached.Sources, [],
                    ToDto(hitMetrics, opts), cached.Quality, false));
            }
        }

        var usage = TokenUsage.Zero;
        long retrievalMs = 0, llmMs = 0, toolMs = 0;
        var llmCalls = 0;
        var toolsUsed = new List<string>();
        var toolOutputs = new List<string>();
        IReadOnlyList<ScoredChunk> chunks = [];

        try
        {
            // ---- 2. Retrieval ----
            var sw = Stopwatch.StartNew();
            var queryEmbedding = await embeddings.EmbedAsync([opts.QueryPrefix + question], ct);
            usage = usage.Add(new TokenUsage(0, 0, queryEmbedding.Tokens));
            chunks = TrimToBudget(await vectorStore.SearchAsync(queryEmbedding.Vectors[0], opts.TopK, opts.MinRelevanceScore, ct), opts.MaxContextTokens);
            retrievalMs = sw.ElapsedMilliseconds;

            // ---- 3. Prompt ----
            var messages = new List<LlmMessage> { LlmMessage.OfSystem(PromptLibrary.SystemPrompt) };
            messages.AddRange(PromptLibrary.FewShotExamples);
            messages.Add(LlmMessage.OfUser(PromptLibrary.BuildUserMessage(question, chunks)));
            var tools = opts.ToolsEnabled ? PromptLibrary.Tools : null;

            // ---- 4/5. Bucle agente: LLM ↔ tools ----
            string? answer = null;
            for (var iteration = 0; iteration <= opts.MaxToolIterations && answer is null; iteration++)
            {
                var allowTools = tools is not null && iteration < opts.MaxToolIterations;
                sw.Restart();
                var response = await llm.ChatAsync(new LlmRequest(messages, allowTools ? tools : null, opts.Temperature, opts.MaxAnswerTokens), ct);
                llmMs += sw.ElapsedMilliseconds;
                llmCalls++;
                usage = usage.Add(response.Usage);

                if (response.ToolCalls.Count == 0 || !allowTools)
                {
                    answer = response.Content?.Trim();
                    break;
                }

                messages.Add(LlmMessage.OfAssistant(response.Content, response.ToolCalls));
                foreach (var call in response.ToolCalls)
                {
                    sw.Restart();
                    var output = await ExecuteToolSafelyAsync(call, command.Context, opts.MaxToolOutputChars, ct);
                    toolMs += sw.ElapsedMilliseconds;
                    toolsUsed.Add(call.Name);
                    toolOutputs.Add(output);
                    messages.Add(LlmMessage.OfTool(call.Id, output));
                }
            }

            answer = string.IsNullOrWhiteSpace(answer) ? "No tengo información suficiente para responder eso." : answer;

            // ---- 6. Validación de calidad ----
            var quality = ResponseValidator.Validate(answer, chunks, toolOutputs);
            if (quality.Checks.Any(c => c.Name == "sin_datos_sensibles" && !c.Passed))
                answer = ResponseValidator.Redact(answer);

            // ---- 7. Métricas y costo ----
            var metrics = new QueryMetrics
            {
                Timestamp = timeProvider.GetUtcNow(), UserId = command.Context.UserId, QuestionPreview = Preview(question),
                Model = llm.ChatModel, PromptVersion = PromptLibrary.PromptVersion,
                TotalLatencyMs = total.ElapsedMilliseconds, RetrievalLatencyMs = retrievalMs, LlmLatencyMs = llmMs, ToolLatencyMs = toolMs,
                PromptTokens = usage.PromptTokens, CachedPromptTokens = usage.CachedPromptTokens, CompletionTokens = usage.CompletionTokens, EmbeddingTokens = usage.EmbeddingTokens,
                EstimatedCostUsd = opts.Pricing.ToPricing().Calculate(usage),
                ReferenceCostUsd = opts.ReferencePricing.ToPricing().Calculate(usage),
                ChunksRetrieved = chunks.Count, ToolsUsed = toolsUsed.Distinct().ToList(), LlmCalls = llmCalls,
                QualityScore = quality.Score, QualityPassed = quality.Passed, Success = true
            };
            await SaveMetricsAsync(metrics, ct);
            await PublishAsync(metrics, command.Context, ct);

            var sources = chunks.Select((c, i) => new SourceDto(i + 1, c.Chunk.Title, c.Chunk.Source, Math.Round(c.Score, 3))).ToList();

            // Solo se cachean respuestas basadas en documentos (no dependen de permisos ni datos vivos).
            if (command.UseCache && toolsUsed.Count == 0 && quality.Passed && !possibleInjection)
                await cache.SetAsync(cacheKey, new CachedAnswer(answer, sources, quality), TimeSpan.FromMinutes(opts.CacheMinutes), ct);

            logger.LogInformation(
                "Consulta IA {QueryId}: {LatencyMs} ms, tokens {PromptTokens}/{CompletionTokens}, tools [{Tools}], calidad {Quality}",
                metrics.Id, metrics.TotalLatencyMs, metrics.PromptTokens, metrics.CompletionTokens, string.Join(",", metrics.ToolsUsed), quality.Score);

            return Result<AskResponse>.Success(new AskResponse(metrics.Id, answer, sources, metrics.ToolsUsed, ToDto(metrics, opts), quality, possibleInjection));
        }
        catch (Exception ex) when (ex is LlmRateLimitedException or LlmUnavailableException)
        {
            await SaveMetricsAsync(new QueryMetrics
            {
                Timestamp = timeProvider.GetUtcNow(), UserId = command.Context.UserId, QuestionPreview = Preview(question),
                Model = llm.ChatModel, PromptVersion = PromptLibrary.PromptVersion, TotalLatencyMs = total.ElapsedMilliseconds,
                PromptTokens = usage.PromptTokens, CompletionTokens = usage.CompletionTokens, EmbeddingTokens = usage.EmbeddingTokens,
                LlmCalls = llmCalls, Success = false, Error = ex.GetType().Name
            }, ct);

            return ex is LlmRateLimitedException
                ? Error.RateLimited("ai.rate_limited", "El modelo está saturado o se alcanzó el límite de uso. Intenta de nuevo en unos segundos.")
                : Error.Unavailable("ai.llm_unavailable", "El modelo de IA no está disponible en este momento.");
        }
    }

    private async Task<string> ExecuteToolSafelyAsync(LlmToolCall call, AgentContext context, int maxChars, CancellationToken ct)
    {
        try
        {
            var output = await toolExecutor.ExecuteAsync(call.Name, call.ArgumentsJson, context, ct);
            return output.Length > maxChars ? output[..maxChars] + "…(truncado)" : output;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falló la herramienta {Tool}", call.Name);
            return $"ERROR: la herramienta {call.Name} no está disponible en este momento.";
        }
    }

    /// <summary>Optimización de tokens: limita el contexto recuperado a un presupuesto.</summary>
    internal static IReadOnlyList<ScoredChunk> TrimToBudget(IReadOnlyList<ScoredChunk> chunks, int maxTokens)
    {
        var result = new List<ScoredChunk>();
        var used = 0;
        foreach (var chunk in chunks.OrderByDescending(c => c.Score))
        {
            var tokens = TextChunker.EstimateTokens(chunk.Chunk.Text);
            if (used + tokens > maxTokens && result.Count > 0) break;
            result.Add(chunk);
            used += tokens;
        }
        return result;
    }

    internal static string CacheKey(string question)
    {
        var normalized = string.Join(' ', question.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('?', '¿', '.', ' ');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{PromptLibrary.PromptVersion}|{normalized}")));
        return $"ai:answer:{hash[..32]}";
    }

    private static string Preview(string question) => question.Length > 120 ? question[..120] + "…" : question;

    private static AnswerMetricsDto ToDto(QueryMetrics m, AgentOptions opts) => new(
        m.TotalLatencyMs, m.RetrievalLatencyMs, m.LlmLatencyMs, m.ToolLatencyMs, m.LlmCalls,
        m.PromptTokens, m.CachedPromptTokens, m.CompletionTokens, m.EmbeddingTokens, m.TotalTokens,
        m.EstimatedCostUsd, m.ReferenceCostUsd, opts.ReferencePricing.Name, m.Model, m.PromptVersion, m.CacheHit);

    private async Task SaveMetricsAsync(QueryMetrics metrics, CancellationToken ct)
    {
        try
        {
            await metricsRepository.SaveAsync(metrics, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudieron guardar las métricas {QueryId}", metrics.Id);
        }
    }

    private Task PublishAsync(QueryMetrics m, AgentContext context, CancellationToken ct) =>
        eventPublisher.PublishAsync(new IntegrationEvent
        {
            EventType = EventTypes.AiQueryCompleted,
            Source = "ai-service",
            ActorId = context.UserId,
            ActorEmail = context.Email,
            SubjectId = m.Id.ToString(),
            Success = m.Success,
            Data = new()
            {
                ["model"] = m.Model,
                ["promptVersion"] = m.PromptVersion,
                ["latencyMs"] = m.TotalLatencyMs.ToString(CultureInfo.InvariantCulture),
                ["promptTokens"] = m.PromptTokens.ToString(CultureInfo.InvariantCulture),
                ["cachedPromptTokens"] = m.CachedPromptTokens.ToString(CultureInfo.InvariantCulture),
                ["estimatedCostUsd"] = m.EstimatedCostUsd.ToString(CultureInfo.InvariantCulture),
                ["completionTokens"] = m.CompletionTokens.ToString(CultureInfo.InvariantCulture),
                ["referenceCostUsd"] = m.ReferenceCostUsd.ToString(CultureInfo.InvariantCulture),
                ["tools"] = string.Join(",", m.ToolsUsed),
                ["qualityScore"] = m.QualityScore.ToString(CultureInfo.InvariantCulture)
            }
        }, ct);
}
