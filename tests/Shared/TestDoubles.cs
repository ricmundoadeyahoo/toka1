using System.Collections.Concurrent;
using System.Text.Json;
using Toka.BuildingBlocks.Contracts.Caching;
using Toka.BuildingBlocks.Contracts.Events;

namespace Toka.Tests.Shared;

/// <summary>Reloj controlable para pruebas deterministas.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;
    public static FixedTimeProvider Default() => new(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}

/// <summary>Publicador en memoria: permite verificar qué eventos se emitieron.</summary>
public sealed class RecordingEventPublisher : IEventPublisher
{
    public List<IntegrationEvent> Events { get; } = [];

    public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(integrationEvent);
        return Task.CompletedTask;
    }

    public IntegrationEvent Single(string eventType) => Events.Single(e => e.EventType == eventType);
    public bool Contains(string eventType) => Events.Any(e => e.EventType == eventType);
}

/// <summary>Caché en memoria con serialización JSON (mismo comportamiento que Redis).</summary>
public sealed class InMemoryCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, string> _store = new();
    public IReadOnlyDictionary<string, string> Store => _store;

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : default);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        _store[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
