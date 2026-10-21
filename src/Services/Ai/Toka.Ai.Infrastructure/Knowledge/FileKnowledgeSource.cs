using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Toka.Ai.Application;
using Toka.Ai.Application.UseCases;

namespace Toka.Ai.Infrastructure.Knowledge;

public sealed class KnowledgeOptions
{
    public const string SectionName = "Knowledge";
    public string Path { get; set; } = "/app/knowledge";
    public string EvaluationDatasetPath { get; set; } = "/app/knowledge/eval/dataset.json";
    public bool IngestOnStartup { get; set; } = true;
}

/// <summary>Base de conocimiento en archivos Markdown (políticas, manuales, FAQ).</summary>
public sealed class FileKnowledgeSource(IOptions<KnowledgeOptions> options) : IKnowledgeSource
{
    public async Task<IReadOnlyList<KnowledgeDocument>> LoadAsync(CancellationToken ct)
    {
        var path = options.Value.Path;
        if (!Directory.Exists(path)) return [];

        var documents = new List<KnowledgeDocument>();
        foreach (var file in Directory.EnumerateFiles(path, "*.md", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal))
        {
            var content = await File.ReadAllTextAsync(file, ct);
            documents.Add(new KnowledgeDocument($"docs/{System.IO.Path.GetFileName(file)}", ExtractTitle(content, file), content));
        }
        return documents;
    }

    internal static string ExtractTitle(string content, string file)
    {
        var heading = content.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal));
        return heading is not null ? heading[2..].Trim() : System.IO.Path.GetFileNameWithoutExtension(file);
    }
}

public sealed class FileEvaluationDatasetSource(IOptions<KnowledgeOptions> options) : IEvaluationDatasetSource
{
    private sealed record CaseDto(string Question, List<string> ExpectedKeywords);

    public async Task<IReadOnlyList<EvaluationCase>> LoadAsync(CancellationToken ct)
    {
        var path = options.Value.EvaluationDatasetPath;
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        var cases = await JsonSerializer.DeserializeAsync<List<CaseDto>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct) ?? new List<CaseDto>();
        return cases.Select(c => new EvaluationCase(c.Question, c.ExpectedKeywords)).ToList();
    }
}

/// <summary>
/// Al arrancar indexa la base de conocimiento si la colección está vacía. Reintenta mientras el servicio de
/// embeddings termina de levantar o de descargar su modelo sin bloquear el arranque de la API.
/// </summary>
public sealed class KnowledgeBootstrapper(
    IServiceScopeFactory scopeFactory,
    IOptions<KnowledgeOptions> options,
    ILogger<KnowledgeBootstrapper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IngestOnStartup) return;

        var delay = TimeSpan.FromSeconds(10);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IVectorStore>();
                if (await store.CountAsync(stoppingToken) > 0)
                {
                    logger.LogInformation("Base de conocimiento ya indexada; no se reindexa al arrancar");
                    return;
                }

                var ingest = scope.ServiceProvider.GetRequiredService<IngestKnowledgeUseCase>();
                var report = await ingest.IngestAllAsync(stoppingToken);
                logger.LogInformation("Indexación inicial: {Documents} documentos, {Chunks} chunks en {ElapsedMs} ms",
                    report.Documents, report.Chunks, report.ElapsedMs);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Indexación inicial pendiente (¿modelo descargándose?): {Error}. Reintento en {Delay}s", ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, 60));
            }
        }
    }
}
