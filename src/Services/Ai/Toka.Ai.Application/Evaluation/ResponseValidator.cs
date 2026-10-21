using System.Text.RegularExpressions;
using Toka.Ai.Application.Prompts;
using Toka.Ai.Domain;

namespace Toka.Ai.Application.Evaluation;

/// <summary>
/// Validación automática (heurística, sin LLM-as-judge para no duplicar costo/latencia) de cada respuesta:
/// no vacía, longitud, fundamentación en el contexto (groundedness), citas, fuga de prompt, datos sensibles
/// e idioma. Peso ≥ 3 = check crítico.
/// </summary>
public static partial class ResponseValidator
{
    public const int MaxAnswerChars = 2000;
    private const double GroundednessThreshold = 0.35;
    private const string NoInfoPhrase = "no tengo información suficiente";

    public static QualityReport Validate(string? answer, IReadOnlyList<ScoredChunk> chunks, IReadOnlyList<string> toolOutputs)
    {
        var text = answer?.Trim() ?? string.Empty;
        var checks = new List<QualityCheck>();

        checks.Add(new("no_vacia", text.Length > 0, text.Length > 0 ? "Respuesta presente" : "Respuesta vacía", 3));
        checks.Add(new("longitud", text.Length <= MaxAnswerChars, $"{text.Length} caracteres (máx {MaxAnswerChars})"));

        var isNoInfo = text.Contains(NoInfoPhrase, StringComparison.OrdinalIgnoreCase);
        var evidence = string.Join("\n", chunks.Select(c => c.Chunk.Text).Concat(toolOutputs));
        if (isNoInfo)
        {
            checks.Add(new("fundamentada", true, "El agente declaró falta de información (no alucina)", 2));
        }
        else if (evidence.Length == 0)
        {
            checks.Add(new("fundamentada", false, "Respondió sin contexto ni herramientas: posible alucinación", 2));
        }
        else
        {
            var ratio = GroundednessRatio(text, evidence);
            checks.Add(new("fundamentada", ratio >= GroundednessThreshold, $"{ratio:P0} de términos clave presentes en las fuentes", 2));
        }

        if (chunks.Count > 0 && toolOutputs.Count == 0 && !isNoInfo)
            checks.Add(new("cita_fuentes", CitationRegex().IsMatch(text), "Debe citar al menos una fuente [n]"));

        checks.Add(new("sin_fuga_prompt", !text.Contains(PromptLibrary.GuardMarker, StringComparison.Ordinal), "No revela instrucciones del sistema", 3));

        var sensitive = SensitiveDataRegex().IsMatch(text);
        checks.Add(new("sin_datos_sensibles", !sensitive, sensitive ? "Contiene patrón de secreto (hash/JWT/contraseña)" : "Sin secretos", 3));

        checks.Add(new("idioma_es", LooksSpanish(text), "Respuesta en español", 0.5));
        return new QualityReport(checks);
    }

    /// <summary>Proporción de términos significativos (≥5 letras) de la respuesta que aparecen en la evidencia.</summary>
    public static double GroundednessRatio(string answer, string evidence)
    {
        var evidenceTerms = Terms(evidence).ToHashSet();
        var answerTerms = Terms(answer).Distinct().ToList();
        if (answerTerms.Count == 0) return 0;
        return Math.Round(answerTerms.Count(evidenceTerms.Contains) / (double)answerTerms.Count, 3);
    }

    /// <summary>Redacta secretos si el modelo llegara a incluirlos (defensa en profundidad).</summary>
    public static string Redact(string text) => SensitiveDataRegex().Replace(text, "[REDACTADO]");

    internal static bool LooksSpanish(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] markers = [" el ", " la ", " de ", " que ", " y ", " en ", " los ", " las ", " para ", " con ", " una ", " no "];
        var padded = $" {text.ToLowerInvariant()} ";
        return markers.Count(m => padded.Contains(m, StringComparison.Ordinal)) >= 2 || padded.Contains('ñ') || padded.Contains('á') || padded.Contains('é') || padded.Contains('ó');
    }

    private static IEnumerable<string> Terms(string text) =>
        WordRegex().Matches(text.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length >= 5);

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex CitationRegex();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();

    // bcrypt, JWT, claves privadas PEM y "password: valor"
    [GeneratedRegex(@"\$2[aby]\$\d{2}\$[./A-Za-z0-9]{53}|eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}|-----BEGIN [A-Z ]*PRIVATE KEY-----|(?i:contrase[ñn]a|password)\s*[:=]\s*\S{6,}")]
    private static partial Regex SensitiveDataRegex();
}

/// <summary>Validación de entrada y detección básica de prompt injection.</summary>
public static partial class InputGuard
{
    public static bool LooksLikeInjection(string question) => InjectionRegex().IsMatch(question);

    [GeneratedRegex(@"(?i)(ignora|olvida|ignore|disregard)\s+(todas\s+)?(las\s+|tus\s+|the\s+|all\s+|previous\s+)*(instrucciones|reglas|instructions|rules)|system\s*prompt|prompt\s+del\s+sistema|act[úu]a\s+como|you\s+are\s+now")]
    private static partial Regex InjectionRegex();
}
