using Toka.Ai.Domain;

namespace Toka.Ai.Application.Evaluation;

public sealed record MetricsSummaryDto(
    int Queries,
    int Successful,
    double CacheHitRate,
    double AvgLatencyMs,
    double P50LatencyMs,
    double P95LatencyMs,
    double AvgLlmLatencyMs,
    long TotalPromptTokens,
    long TotalCompletionTokens,
    long TotalEmbeddingTokens,
    double AvgTokensPerQuery,
    decimal TotalEstimatedCostUsd,
    decimal TotalReferenceCostUsd,
    double AvgQualityScore,
    double QualityPassRate,
    IReadOnlyDictionary<string, int> ToolUsage);

/// <summary>Agregados de evaluación continua (latencia p50/p95, tokens, costo, calidad, cache).</summary>
public static class MetricsSummaryCalculator
{
    public static MetricsSummaryDto Calculate(IReadOnlyList<QueryMetrics> metrics)
    {
        if (metrics.Count == 0)
            return new MetricsSummaryDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new Dictionary<string, int>());

        var successful = metrics.Where(m => m.Success).ToList();
        var latencies = successful.Select(m => (double)m.TotalLatencyMs).OrderBy(x => x).ToList();
        var nonCached = successful.Where(m => !m.CacheHit).ToList();

        return new MetricsSummaryDto(
            metrics.Count,
            successful.Count,
            Math.Round(metrics.Count(m => m.CacheHit) / (double)metrics.Count, 3),
            latencies.Count == 0 ? 0 : Math.Round(latencies.Average(), 1),
            Percentile(latencies, 0.50),
            Percentile(latencies, 0.95),
            nonCached.Count == 0 ? 0 : Math.Round(nonCached.Average(m => m.LlmLatencyMs), 1),
            metrics.Sum(m => (long)m.PromptTokens),
            metrics.Sum(m => (long)m.CompletionTokens),
            metrics.Sum(m => (long)m.EmbeddingTokens),
            Math.Round(metrics.Average(m => m.TotalTokens), 1),
            metrics.Sum(m => m.EstimatedCostUsd),
            metrics.Sum(m => m.ReferenceCostUsd),
            successful.Count == 0 ? 0 : Math.Round(successful.Average(m => m.QualityScore), 3),
            successful.Count == 0 ? 0 : Math.Round(successful.Count(m => m.QualityPassed) / (double)successful.Count, 3),
            metrics.SelectMany(m => m.ToolsUsed).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count()));
    }

    /// <summary>Percentil por el método nearest-rank sobre una lista ordenada.</summary>
    public static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        if (percentile is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(percentile));
        var rank = (int)Math.Ceiling(percentile * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }
}
