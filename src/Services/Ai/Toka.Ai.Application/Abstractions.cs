using Toka.Ai.Domain;

namespace Toka.Ai.Application;

// ---------------- LLM ----------------

public sealed record LlmToolCall(string Id, string Name, string ArgumentsJson);

public sealed record LlmMessage(string Role, string? Content, IReadOnlyList<LlmToolCall>? ToolCalls = null, string? ToolCallId = null)
{
    public static LlmMessage OfSystem(string content) => new("system", content);
    public static LlmMessage OfUser(string content) => new("user", content);
    public static LlmMessage OfAssistant(string? content, IReadOnlyList<LlmToolCall>? toolCalls = null) => new("assistant", content, toolCalls);
    public static LlmMessage OfTool(string toolCallId, string content) => new("tool", content, null, toolCallId);
}

/// <summary>Definición de herramienta (function calling). ParametersJsonSchema es un JSON Schema.</summary>
public sealed record ToolDefinition(string Name, string Description, string ParametersJsonSchema);

public sealed record LlmRequest(
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<ToolDefinition>? Tools = null,
    double Temperature = 0.2,
    int MaxTokens = 512);

public sealed record LlmResponse(string? Content, IReadOnlyList<LlmToolCall> ToolCalls, TokenUsage Usage, string Model, string? FinishReason);

/// <summary>
/// Puerto del modelo de lenguaje. La implementación habla el protocolo OpenAI Chat Completions,
/// que exponen DeepSeek, OpenAI, Azure OpenAI (v1) y Ollama: cambiar de proveedor es configuración.
/// </summary>
public interface ILlmClient
{
    string ChatModel { get; }
    Task<LlmResponse> ChatAsync(LlmRequest request, CancellationToken ct);
}

public sealed record EmbeddingResult(IReadOnlyList<float[]> Vectors, int Tokens);

public interface IEmbeddingClient
{
    string EmbeddingModel { get; }
    Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct);
}

/// <summary>El proveedor LLM rechazó por cuota/concurrencia (HTTP 429 o cola local saturada).</summary>
public sealed class LlmRateLimitedException(string message, TimeSpan? retryAfter = null) : Exception(message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>El proveedor LLM no está disponible (caído, circuit breaker abierto, timeout).</summary>
public sealed class LlmUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

// ---------------- RAG ----------------

public interface IVectorStore
{
    Task EnsureCollectionAsync(int vectorSize, CancellationToken ct);
    Task UpsertAsync(IReadOnlyList<(KnowledgeChunk Chunk, float[] Vector)> points, CancellationToken ct);
    Task<IReadOnlyList<ScoredChunk>> SearchAsync(float[] vector, int limit, double minScore, CancellationToken ct);
    Task DeleteBySourceAsync(string source, CancellationToken ct);
    Task<long> CountAsync(CancellationToken ct);
}

public sealed record KnowledgeDocument(string Source, string Title, string Content);

public interface IKnowledgeSource
{
    Task<IReadOnlyList<KnowledgeDocument>> LoadAsync(CancellationToken ct);
}

// ---------------- Agente / tools ----------------

/// <summary>Contexto del usuario que pregunta: su token se reenvía a los microservicios (token relay).</summary>
public sealed record AgentContext(string? UserId, string? Email, string? BearerToken, IReadOnlyList<string> Permissions);

public interface IAgentToolExecutor
{
    Task<string> ExecuteAsync(string toolName, string argumentsJson, AgentContext context, CancellationToken ct);
}

// ---------------- Métricas ----------------

public interface IMetricsRepository
{
    Task SaveAsync(QueryMetrics metrics, CancellationToken ct);
    Task<IReadOnlyList<QueryMetrics>> GetRecentAsync(int limit, CancellationToken ct);
}

// ---------------- Configuración ----------------

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public int TopK { get; set; } = 4;
    public double MinRelevanceScore { get; set; } = 0.45;
    public int MaxContextTokens { get; set; } = 1500;
    public int MaxToolIterations { get; set; } = 3;
    public int MaxToolOutputChars { get; set; } = 2000;
    public int MaxQuestionLength { get; set; } = 1000;
    public int MaxAnswerTokens { get; set; } = 512;
    public double Temperature { get; set; } = 0.2;
    public bool ToolsEnabled { get; set; } = true;
    public int CacheMinutes { get; set; } = 60;
    public int ChunkSize { get; set; } = 900;
    public int ChunkOverlap { get; set; } = 150;
    public string QueryPrefix { get; set; } = "search_query: ";
    public string DocumentPrefix { get; set; } = "search_document: ";

    /// <summary>
    /// Precio real del modelo configurado. Default: deepseek-flash a tarifa PICO (conservador; la tarifa valle es la mitad).
    /// Verificar precios vigentes en https://api-docs.deepseek.com/quick_start/pricing
    /// </summary>
    public PricingOptions Pricing { get; set; } = new() { Name = "deepseek-flash (tarifa pico)", InputPerMillionUsd = 0.30m, CachedInputPerMillionUsd = 0.006m, OutputPerMillionUsd = 1.20m };

    /// <summary>Precio de referencia de un modelo en la nube, para estimar el ahorro / costo equivalente.</summary>
    public PricingOptions ReferencePricing { get; set; } = new() { Name = "gpt-4o-mini (referencia)", InputPerMillionUsd = 0.15m, OutputPerMillionUsd = 0.60m, EmbeddingPerMillionUsd = 0.02m };
}

public sealed class PricingOptions
{
    public string Name { get; set; } = "local";
    public decimal InputPerMillionUsd { get; set; }
    /// <summary>Precio de entrada con cache hit (null = mismo que InputPerMillionUsd).</summary>
    public decimal? CachedInputPerMillionUsd { get; set; }
    public decimal OutputPerMillionUsd { get; set; }
    public decimal EmbeddingPerMillionUsd { get; set; }

    public ModelPricing ToPricing() => new(InputPerMillionUsd, OutputPerMillionUsd, EmbeddingPerMillionUsd, CachedInputPerMillionUsd);
}
