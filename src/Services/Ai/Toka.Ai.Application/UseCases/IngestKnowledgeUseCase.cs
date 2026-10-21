using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Toka.Ai.Application.Rag;
using Toka.Ai.Domain;
using Toka.BuildingBlocks.Contracts.Events;
using Toka.BuildingBlocks.Contracts.Results;
using Toka.BuildingBlocks.Contracts.Validation;

namespace Toka.Ai.Application.UseCases;

public sealed record IngestionReport(int Documents, int Chunks, int EmbeddingTokens, long ElapsedMs, string EmbeddingModel);

/// <summary>
/// Pipeline de embeddings: documentos → chunking → embeddings (en lotes) → upsert en Qdrant.
/// Idempotente: los Ids de los chunks son deterministas y se borran los de la fuente antes de reinsertar.
/// </summary>
public sealed class IngestKnowledgeUseCase(
    IKnowledgeSource knowledgeSource,
    IEmbeddingClient embeddingClient,
    IVectorStore vectorStore,
    IEventPublisher eventPublisher,
    IOptions<AgentOptions> options,
    ILogger<IngestKnowledgeUseCase> logger)
{
    private const int BatchSize = 16;

    public async Task<IngestionReport> IngestAllAsync(CancellationToken ct)
    {
        var documents = await knowledgeSource.LoadAsync(ct);
        return await IngestAsync(documents, ct);
    }

    public async Task<Result<IngestionReport>> IngestDocumentAsync(string title, string content, CancellationToken ct)
    {
        var titleError = TextInput.Validate(title, "El título", 3, 120);
        if (titleError is not null) return Error.Validation("ai.title_invalid", titleError);
        if (string.IsNullOrWhiteSpace(content) || content.Length < 20 || content.Length > 100_000)
            return Error.Validation("ai.content_invalid", "El contenido debe tener entre 20 y 100,000 caracteres.");

        var slug = new string(title.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        var document = new KnowledgeDocument($"api/{slug}", title.Trim(), TextInput.Sanitize(content, 100_000));
        return Result<IngestionReport>.Success(await IngestAsync([document], ct));
    }

    public async Task<IngestionReport> IngestAsync(IReadOnlyList<KnowledgeDocument> documents, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var opts = options.Value;
        var totalChunks = 0;
        var totalTokens = 0;

        foreach (var document in documents)
        {
            var chunks = TextChunker.Chunk(document, opts.ChunkSize, opts.ChunkOverlap);
            if (chunks.Count == 0) continue;

            var points = new List<(KnowledgeChunk, float[])>(chunks.Count);
            foreach (var batch in chunks.Chunk(BatchSize))
            {
                var embeddings = await embeddingClient.EmbedAsync(batch.Select(c => opts.DocumentPrefix + c.Text).ToList(), ct);
                if (embeddings.Vectors.Count != batch.Length)
                    throw new InvalidOperationException("El servicio de embeddings devolvió una cantidad inesperada de vectores.");
                totalTokens += embeddings.Tokens;
                points.AddRange(batch.Select((chunk, i) => (chunk, embeddings.Vectors[i])));
            }

            await vectorStore.EnsureCollectionAsync(points[0].Item2.Length, ct);
            await vectorStore.DeleteBySourceAsync(document.Source, ct);
            await vectorStore.UpsertAsync(points, ct);
            totalChunks += chunks.Count;
            logger.LogInformation("Documento indexado {Source}: {Chunks} chunks", document.Source, chunks.Count);
        }

        var report = new IngestionReport(documents.Count, totalChunks, totalTokens, sw.ElapsedMilliseconds, embeddingClient.EmbeddingModel);
        await eventPublisher.PublishAsync(new IntegrationEvent
        {
            EventType = EventTypes.AiKnowledgeIngested,
            Source = "ai-service",
            Data = new()
            {
                ["documents"] = report.Documents.ToString(),
                ["chunks"] = report.Chunks.ToString(),
                ["embeddingTokens"] = report.EmbeddingTokens.ToString(),
                ["elapsedMs"] = report.ElapsedMs.ToString()
            }
        }, ct);
        return report;
    }
}
