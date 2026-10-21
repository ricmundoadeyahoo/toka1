using System.Security.Cryptography;
using System.Text;
using Toka.Ai.Domain;

namespace Toka.Ai.Application.Rag;

/// <summary>
/// Chunking consciente de Markdown: corta por secciones (#, ##) y luego por párrafos,
/// respetando un tamaño máximo con solapamiento para no perder contexto entre fragmentos.
/// Cada chunk antepone el título de su sección, lo que mejora la recuperación semántica.
/// </summary>
public static class TextChunker
{
    public static IReadOnlyList<KnowledgeChunk> Chunk(KnowledgeDocument document, int maxChars = 900, int overlap = 150)
    {
        if (maxChars < 100) throw new ArgumentOutOfRangeException(nameof(maxChars), "maxChars debe ser ≥ 100.");
        if (overlap < 0 || overlap >= maxChars) throw new ArgumentOutOfRangeException(nameof(overlap));

        var chunks = new List<KnowledgeChunk>();
        var index = 0;
        foreach (var (heading, body) in SplitSections(document.Content))
        {
            var prefix = string.IsNullOrEmpty(heading) ? document.Title : $"{document.Title} › {heading}";
            foreach (var piece in SplitBySize(body, maxChars, overlap))
            {
                var text = $"{prefix}\n{piece}".Trim();
                chunks.Add(new KnowledgeChunk(DeterministicId(document.Source, index), document.Source, document.Title, index, text));
                index++;
            }
        }
        return chunks;
    }

    internal static IEnumerable<(string Heading, string Body)> SplitSections(string content)
    {
        var heading = string.Empty;
        var buffer = new StringBuilder();
        foreach (var rawLine in (content ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith('#'))
            {
                if (buffer.ToString().Trim().Length > 0) yield return (heading, buffer.ToString().Trim());
                buffer.Clear();
                heading = line.TrimStart('#').Trim();
                continue;
            }
            buffer.AppendLine(line);
        }
        if (buffer.ToString().Trim().Length > 0) yield return (heading, buffer.ToString().Trim());
    }

    internal static IEnumerable<string> SplitBySize(string text, int maxChars, int overlap)
    {
        if (text.Length <= maxChars)
        {
            yield return text;
            yield break;
        }

        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = new StringBuilder();
        foreach (var paragraph in paragraphs.SelectMany(p => p.Length > maxChars ? HardSplit(p, maxChars, overlap) : new[] { p }))
        {
            if (current.Length > 0 && current.Length + paragraph.Length + 2 > maxChars)
            {
                var emitted = current.ToString().Trim();
                yield return emitted;
                current.Clear();
                if (overlap > 0) current.Append(emitted[Math.Max(0, emitted.Length - overlap)..]).Append("\n\n");
            }
            current.Append(paragraph).Append("\n\n");
        }
        if (current.ToString().Trim().Length > 0) yield return current.ToString().Trim();
    }

    private static IEnumerable<string> HardSplit(string text, int maxChars, int overlap)
    {
        var step = maxChars - overlap;
        for (var start = 0; start < text.Length; start += step)
        {
            yield return text.Substring(start, Math.Min(maxChars, text.Length - start));
            if (start + maxChars >= text.Length) yield break;
        }
    }

    /// <summary>Id estable (source + índice) → reindexar es idempotente (upsert sobre los mismos puntos).</summary>
    public static Guid DeterministicId(string source, int index)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"{source}#{index}"));
        return new Guid(hash);
    }

    /// <summary>Estimación rápida de tokens (~4 caracteres por token en español/inglés).</summary>
    public static int EstimateTokens(string? text) => string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);
}
