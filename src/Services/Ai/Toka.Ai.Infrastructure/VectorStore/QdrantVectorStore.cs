using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Toka.Ai.Application;
using Toka.Ai.Domain;

namespace Toka.Ai.Infrastructure.VectorStore;

public sealed class QdrantOptions
{
    public const string SectionName = "Qdrant";
    public string BaseUrl { get; set; } = "http://qdrant:6333";
    public string Collection { get; set; } = "toka_knowledge";
    public string? ApiKey { get; set; }
}

/// <summary>Vector DB Qdrant vía su API REST (distancia coseno, payload con el texto y metadatos del chunk).</summary>
public sealed class QdrantVectorStore : IVectorStore
{
    private readonly HttpClient _http;
    private readonly QdrantOptions _options;
    private readonly ILogger<QdrantVectorStore> _logger;

    public QdrantVectorStore(HttpClient http, IOptions<QdrantOptions> options, ILogger<QdrantVectorStore> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(_options.ApiKey)) _http.DefaultRequestHeaders.Add("api-key", _options.ApiKey);
    }

    private string CollectionPath => $"collections/{Uri.EscapeDataString(_options.Collection)}";

    public async Task EnsureCollectionAsync(int vectorSize, CancellationToken ct)
    {
        using var existing = await _http.GetAsync(CollectionPath, ct);
        if (existing.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(await existing.Content.ReadAsStringAsync(ct));
            var currentSize = TryGetVectorSize(doc.RootElement);
            if (currentSize is null || currentSize == vectorSize) return;

            _logger.LogWarning("La colección {Collection} tiene dimensión {Current} y el modelo produce {Expected}; se recrea",
                _options.Collection, currentSize, vectorSize);
            (await _http.DeleteAsync(CollectionPath, ct)).EnsureSuccessStatusCode();
        }
        else if (existing.StatusCode != HttpStatusCode.NotFound)
        {
            existing.EnsureSuccessStatusCode();
        }

        var create = await _http.PutAsJsonAsync(CollectionPath, new { vectors = new { size = vectorSize, distance = "Cosine" } }, ct);
        if (!create.IsSuccessStatusCode && create.StatusCode != HttpStatusCode.Conflict) create.EnsureSuccessStatusCode();

        // Índice de payload para borrar/filtrar por fuente eficientemente.
        await _http.PutAsJsonAsync($"{CollectionPath}/index", new { field_name = "source", field_schema = "keyword" }, ct);
        _logger.LogInformation("Colección Qdrant {Collection} creada (dim {Dim})", _options.Collection, vectorSize);
    }

    public async Task UpsertAsync(IReadOnlyList<(KnowledgeChunk Chunk, float[] Vector)> points, CancellationToken ct)
    {
        foreach (var batch in points.Chunk(64))
        {
            var body = new
            {
                points = batch.Select(p => new
                {
                    id = p.Chunk.Id.ToString(),
                    vector = p.Vector,
                    payload = new { source = p.Chunk.Source, title = p.Chunk.Title, index = p.Chunk.Index, text = p.Chunk.Text }
                })
            };
            using var response = await _http.PutAsJsonAsync($"{CollectionPath}/points?wait=true", body, ct);
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(float[] vector, int limit, double minScore, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync($"{CollectionPath}/points/search",
            new { vector, limit, with_payload = true, score_threshold = minScore }, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var results = new List<ScoredChunk>();
        foreach (var item in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            var payload = item.GetProperty("payload");
            var chunk = new KnowledgeChunk(
                Guid.TryParse(item.GetProperty("id").ToString(), out var id) ? id : Guid.Empty,
                payload.GetProperty("source").GetString() ?? string.Empty,
                payload.GetProperty("title").GetString() ?? string.Empty,
                payload.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0,
                payload.GetProperty("text").GetString() ?? string.Empty);
            results.Add(new ScoredChunk(chunk, item.GetProperty("score").GetDouble()));
        }
        return results;
    }

    public async Task DeleteBySourceAsync(string source, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync($"{CollectionPath}/points/delete?wait=true",
            new { filter = new { must = new[] { new { key = "source", match = new { value = source } } } } }, ct);
        if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }

    public async Task<long> CountAsync(CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync($"{CollectionPath}/points/count", new { exact = true }, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return 0;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("result").GetProperty("count").GetInt64();
    }

    private static int? TryGetVectorSize(JsonElement root)
    {
        try
        {
            var vectors = root.GetProperty("result").GetProperty("config").GetProperty("params").GetProperty("vectors");
            return vectors.TryGetProperty("size", out var size) ? size.GetInt32() : null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }
}
