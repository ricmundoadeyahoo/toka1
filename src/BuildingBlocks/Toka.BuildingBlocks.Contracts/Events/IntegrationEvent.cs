namespace Toka.BuildingBlocks.Contracts.Events;

/// <summary>
/// Envelope común para todos los eventos de integración publicados en RabbitMQ.
/// Usar un envelope homogéneo permite que Audit Service persista cualquier evento sin
/// conocer su esquema, y que los consumidores sean tolerantes a campos nuevos.
/// </summary>
public sealed record IntegrationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string EventType { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public required string Source { get; init; }
    public string? CorrelationId { get; init; }
    public string? ActorId { get; init; }
    public string? ActorEmail { get; init; }
    public string? SubjectId { get; init; }
    public string? IpAddress { get; init; }
    public bool Success { get; init; } = true;
    public Dictionary<string, string> Data { get; init; } = new();
    public int Version { get; init; } = 1;
}
