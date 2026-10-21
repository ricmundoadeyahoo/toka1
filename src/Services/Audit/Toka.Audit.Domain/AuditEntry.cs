namespace Toka.Audit.Domain;

/// <summary>
/// Registro de auditoría INMUTABLE (append-only). Su Id es el Id del evento de integración,
/// lo que hace la ingesta idempotente: un evento reentregado no genera duplicados.
/// </summary>
public sealed class AuditEntry
{
    public const int MaxDetailValueLength = 500;
    public const int MaxDetails = 30;

    private AuditEntry() { }

    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public string? ActorId { get; private set; }
    public string? ActorEmail { get; private set; }
    public string? SubjectId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? CorrelationId { get; private set; }
    public bool Success { get; private set; }
    public Dictionary<string, string> Details { get; private set; } = new();

    public static AuditEntry Create(
        Guid id, string eventType, DateTimeOffset occurredAt, DateTimeOffset recordedAt, string source,
        string? actorId, string? actorEmail, string? subjectId, string? ipAddress, string? correlationId,
        bool success, IReadOnlyDictionary<string, string>? details)
    {
        if (id == Guid.Empty) throw new ArgumentException("El Id del evento es obligatorio.", nameof(id));
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("El tipo de evento es obligatorio.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("El origen es obligatorio.", nameof(source));

        return new AuditEntry
        {
            Id = id,
            EventType = eventType.Trim(),
            Category = eventType.Split('.', 2)[0],
            OccurredAt = occurredAt,
            RecordedAt = recordedAt,
            Source = source,
            ActorId = actorId,
            ActorEmail = actorEmail,
            SubjectId = subjectId,
            IpAddress = ipAddress,
            CorrelationId = correlationId,
            Success = success,
            Details = SanitizeDetails(details)
        };
    }

    /// <summary>Recorta valores largos, limita cantidad y elimina claves sensibles (defensa en profundidad).</summary>
    internal static Dictionary<string, string> SanitizeDetails(IReadOnlyDictionary<string, string>? details)
    {
        var result = new Dictionary<string, string>();
        if (details is null) return result;

        foreach (var (key, value) in details.Take(MaxDetails))
        {
            if (IsSensitive(key)) { result[key] = "***"; continue; }
            var v = value ?? string.Empty;
            result[key] = v.Length > MaxDetailValueLength ? v[..MaxDetailValueLength] + "…" : v;
        }
        return result;
    }

    private static bool IsSensitive(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase);
}
