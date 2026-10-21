using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Toka.Ai.Application;
using Toka.Ai.Domain;

namespace Toka.Ai.Infrastructure.Metrics;

public sealed class AiMongoOptions
{
    public const string SectionName = "Mongo";
    public string ConnectionString { get; set; } = "mongodb://mongo:27017";
    public string Database { get; set; } = "ai_db";
    public string MetricsCollection { get; set; } = "query_metrics";
}

internal sealed class MetricsDocument
{
    [BsonId] public string Id { get; set; } = string.Empty;
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)] public DateTime Timestamp { get; set; }
    public string? UserId { get; set; }
    public string QuestionPreview { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public long TotalLatencyMs { get; set; }
    public long RetrievalLatencyMs { get; set; }
    public long LlmLatencyMs { get; set; }
    public long ToolLatencyMs { get; set; }
    public int PromptTokens { get; set; }
    public int CachedPromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int EmbeddingTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public decimal ReferenceCostUsd { get; set; }
    public int ChunksRetrieved { get; set; }
    public List<string> ToolsUsed { get; set; } = [];
    public int LlmCalls { get; set; }
    public double QualityScore { get; set; }
    public bool QualityPassed { get; set; }
    public bool CacheHit { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }

    public static MetricsDocument From(QueryMetrics m) => new()
    {
        Id = m.Id.ToString(), Timestamp = m.Timestamp.UtcDateTime, UserId = m.UserId, QuestionPreview = m.QuestionPreview,
        Model = m.Model, PromptVersion = m.PromptVersion, TotalLatencyMs = m.TotalLatencyMs, RetrievalLatencyMs = m.RetrievalLatencyMs,
        LlmLatencyMs = m.LlmLatencyMs, ToolLatencyMs = m.ToolLatencyMs, PromptTokens = m.PromptTokens, CachedPromptTokens = m.CachedPromptTokens, CompletionTokens = m.CompletionTokens,
        EmbeddingTokens = m.EmbeddingTokens, EstimatedCostUsd = m.EstimatedCostUsd, ReferenceCostUsd = m.ReferenceCostUsd,
        ChunksRetrieved = m.ChunksRetrieved, ToolsUsed = m.ToolsUsed.ToList(), LlmCalls = m.LlmCalls, QualityScore = m.QualityScore,
        QualityPassed = m.QualityPassed, CacheHit = m.CacheHit, Success = m.Success, Error = m.Error
    };

    public QueryMetrics ToDomain() => new()
    {
        Id = Guid.Parse(Id), Timestamp = new DateTimeOffset(Timestamp, TimeSpan.Zero), UserId = UserId, QuestionPreview = QuestionPreview,
        Model = Model, PromptVersion = PromptVersion, TotalLatencyMs = TotalLatencyMs, RetrievalLatencyMs = RetrievalLatencyMs,
        LlmLatencyMs = LlmLatencyMs, ToolLatencyMs = ToolLatencyMs, PromptTokens = PromptTokens, CachedPromptTokens = CachedPromptTokens, CompletionTokens = CompletionTokens,
        EmbeddingTokens = EmbeddingTokens, EstimatedCostUsd = EstimatedCostUsd, ReferenceCostUsd = ReferenceCostUsd,
        ChunksRetrieved = ChunksRetrieved, ToolsUsed = ToolsUsed, LlmCalls = LlmCalls, QualityScore = QualityScore,
        QualityPassed = QualityPassed, CacheHit = CacheHit, Success = Success, Error = Error
    };
}

/// <summary>Métricas de cada consulta en MongoDB (documentos flexibles, consultas por fecha).</summary>
public sealed class MongoMetricsRepository : IMetricsRepository
{
    private readonly IMongoCollection<MetricsDocument> _collection;

    public MongoMetricsRepository(IMongoClient client, IOptions<AiMongoOptions> options)
    {
        _collection = client.GetDatabase(options.Value.Database).GetCollection<MetricsDocument>(options.Value.MetricsCollection);
    }

    public Task SaveAsync(QueryMetrics metrics, CancellationToken ct) =>
        _collection.InsertOneAsync(MetricsDocument.From(metrics), cancellationToken: ct);

    public async Task<IReadOnlyList<QueryMetrics>> GetRecentAsync(int limit, CancellationToken ct)
    {
        var docs = await _collection.Find(FilterDefinition<MetricsDocument>.Empty)
            .SortByDescending(d => d.Timestamp)
            .Limit(Math.Clamp(limit, 1, 1000))
            .ToListAsync(ct);
        return docs.Select(d => d.ToDomain()).ToList();
    }

    public Task EnsureIndexesAsync(CancellationToken ct) =>
        _collection.Indexes.CreateOneAsync(
            new CreateIndexModel<MetricsDocument>(Builders<MetricsDocument>.IndexKeys.Descending(d => d.Timestamp)),
            cancellationToken: ct);
}
