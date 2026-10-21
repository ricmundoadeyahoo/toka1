using Microsoft.Extensions.DependencyInjection;
using Toka.Ai.Application.Evaluation;
using Toka.Ai.Domain;

namespace Toka.Ai.Application.UseCases;

public sealed record EvaluationCase(string Question, IReadOnlyList<string> ExpectedKeywords);

public sealed record EvaluationCaseResult(
    string Question, string Answer, double KeywordRecall, double QualityScore, bool Passed,
    long LatencyMs, int TotalTokens, decimal ReferenceCostUsd, string? Error);

public sealed record EvaluationReport(
    int Cases, int Passed, double PassRate, double AvgKeywordRecall, double AvgQualityScore,
    double AvgLatencyMs, double P95LatencyMs, long TotalTokens, decimal TotalReferenceCostUsd,
    IReadOnlyList<EvaluationCaseResult> Results);

public interface IEvaluationDatasetSource
{
    Task<IReadOnlyList<EvaluationCase>> LoadAsync(CancellationToken ct);
}

/// <summary>
/// Evaluación offline contra un "golden dataset" (preguntas + palabras clave esperadas).
/// Útil como prueba de regresión al cambiar de modelo, prompt o parámetros de RAG.
/// </summary>
public sealed class EvaluateAgentUseCase(AskAgentUseCase agent, IEvaluationDatasetSource dataset)
{
    public const double KeywordRecallThreshold = 0.5;

    public async Task<EvaluationReport> ExecuteAsync(AgentContext context, int? maxCases, CancellationToken ct)
    {
        var cases = (await dataset.LoadAsync(ct)).Take(Math.Clamp(maxCases ?? 10, 1, 25)).ToList();
        var results = new List<EvaluationCaseResult>();

        foreach (var testCase in cases)
        {
            var result = await agent.ExecuteAsync(new AskCommand(testCase.Question, UseCache: false, context), ct);
            if (result.IsFailure)
            {
                results.Add(new EvaluationCaseResult(testCase.Question, string.Empty, 0, 0, false, 0, 0, 0, result.Error!.Message));
                continue;
            }

            var r = result.Value;
            var recall = KeywordRecall(r.Answer, testCase.ExpectedKeywords);
            results.Add(new EvaluationCaseResult(
                testCase.Question, r.Answer, recall, r.Quality.Score,
                recall >= KeywordRecallThreshold && r.Quality.Passed,
                r.Metrics.LatencyMs, r.Metrics.TotalTokens, r.Metrics.ReferenceCostUsd, null));
        }

        return BuildReport(results);
    }

    internal static EvaluationReport BuildReport(IReadOnlyList<EvaluationCaseResult> results)
    {
        if (results.Count == 0) return new EvaluationReport(0, 0, 0, 0, 0, 0, 0, 0, 0, results);
        var latencies = results.Where(r => r.Error is null).Select(r => (double)r.LatencyMs).OrderBy(x => x).ToList();
        var passed = results.Count(r => r.Passed);
        return new EvaluationReport(
            results.Count,
            passed,
            Math.Round(passed / (double)results.Count, 3),
            Math.Round(results.Average(r => r.KeywordRecall), 3),
            Math.Round(results.Average(r => r.QualityScore), 3),
            latencies.Count == 0 ? 0 : Math.Round(latencies.Average(), 1),
            MetricsSummaryCalculator.Percentile(latencies, 0.95),
            results.Sum(r => (long)r.TotalTokens),
            results.Sum(r => r.ReferenceCostUsd),
            results);
    }

    /// <summary>Fracción de palabras clave esperadas presentes en la respuesta (sin distinguir mayúsculas/acentos).</summary>
    public static double KeywordRecall(string answer, IReadOnlyList<string> expected)
    {
        if (expected.Count == 0) return 1;
        var normalizedAnswer = Normalize(answer);
        return Math.Round(expected.Count(k => normalizedAnswer.Contains(Normalize(k), StringComparison.Ordinal)) / (double)expected.Count, 3);
    }

    internal static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddAiApplication(this IServiceCollection services)
    {
        services.AddScoped<IngestKnowledgeUseCase>();
        services.AddScoped<AskAgentUseCase>();
        services.AddScoped<EvaluateAgentUseCase>();
        return services;
    }
}
