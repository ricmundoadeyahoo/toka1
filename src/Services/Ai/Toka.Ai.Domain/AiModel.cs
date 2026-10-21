namespace Toka.Ai.Domain;

/// <summary>Fragmento de conocimiento indexado en la base vectorial.</summary>
public sealed record KnowledgeChunk(Guid Id, string Source, string Title, int Index, string Text);

public sealed record ScoredChunk(KnowledgeChunk Chunk, double Score);

/// <summary>
/// Consumo de tokens. CachedPromptTokens es la parte de PromptTokens servida desde la caché de contexto
/// del proveedor (DeepSeek/OpenAI la cobran mucho más barata).
/// </summary>
public sealed record TokenUsage(int PromptTokens, int CompletionTokens, int EmbeddingTokens = 0, int CachedPromptTokens = 0)
{
    public static readonly TokenUsage Zero = new(0, 0, 0, 0);
    public int TotalTokens => PromptTokens + CompletionTokens + EmbeddingTokens;

    public TokenUsage Add(TokenUsage other) => new(
        PromptTokens + other.PromptTokens,
        CompletionTokens + other.CompletionTokens,
        EmbeddingTokens + other.EmbeddingTokens,
        CachedPromptTokens + other.CachedPromptTokens);
}

/// <summary>
/// Value Object de precios por millón de tokens (USD). CachedInputPerMillion es el precio de los tokens de entrada
/// con cache hit; si es null se cobran al precio normal de entrada.
/// </summary>
public sealed record ModelPricing(decimal InputPerMillion, decimal OutputPerMillion, decimal EmbeddingPerMillion = 0, decimal? CachedInputPerMillion = null)
{
    public decimal Calculate(TokenUsage usage)
    {
        if (InputPerMillion < 0 || OutputPerMillion < 0 || EmbeddingPerMillion < 0 || CachedInputPerMillion < 0)
            throw new ArgumentException("Los precios no pueden ser negativos.");
        var cached = Math.Clamp(usage.CachedPromptTokens, 0, usage.PromptTokens);
        var cost = (usage.PromptTokens - cached) * InputPerMillion / 1_000_000m
                   + cached * (CachedInputPerMillion ?? InputPerMillion) / 1_000_000m
                   + usage.CompletionTokens * OutputPerMillion / 1_000_000m
                   + usage.EmbeddingTokens * EmbeddingPerMillion / 1_000_000m;
        return Math.Round(cost, 8);
    }
}

public sealed record QualityCheck(string Name, bool Passed, string Detail, double Weight = 1.0);

/// <summary>Resultado de la validación automática de una respuesta del agente.</summary>
public sealed record QualityReport(IReadOnlyList<QualityCheck> Checks)
{
    public double Score
    {
        get
        {
            var total = Checks.Sum(c => c.Weight);
            return total <= 0 ? 0 : Math.Round(Checks.Where(c => c.Passed).Sum(c => c.Weight) / total, 3);
        }
    }

    /// <summary>Aprobada si el puntaje es suficiente y ningún check crítico (peso ≥ 3) falló.</summary>
    public bool Passed => Score >= 0.6 && Checks.All(c => c.Passed || c.Weight < 3);
}

/// <summary>Métricas de una consulta al agente (latencia, tokens, costo, calidad).</summary>
public sealed record QueryMetrics
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; }
    public string? UserId { get; init; }
    public string QuestionPreview { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = string.Empty;
    public long TotalLatencyMs { get; init; }
    public long RetrievalLatencyMs { get; init; }
    public long LlmLatencyMs { get; init; }
    public long ToolLatencyMs { get; init; }
    public int PromptTokens { get; init; }
    public int CachedPromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int EmbeddingTokens { get; init; }
    public decimal EstimatedCostUsd { get; init; }
    public decimal ReferenceCostUsd { get; init; }
    public int ChunksRetrieved { get; init; }
    public IReadOnlyList<string> ToolsUsed { get; init; } = [];
    public int LlmCalls { get; init; }
    public double QualityScore { get; init; }
    public bool QualityPassed { get; init; }
    public bool CacheHit { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }

    public int TotalTokens => PromptTokens + CompletionTokens + EmbeddingTokens;
}
