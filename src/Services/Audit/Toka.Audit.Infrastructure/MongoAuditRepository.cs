using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Toka.Audit.Application;
using Toka.Audit.Domain;
using Toka.BuildingBlocks.Infrastructure.Hosting;
using Toka.BuildingBlocks.Infrastructure.Messaging;

namespace Toka.Audit.Infrastructure;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";
    public string ConnectionString { get; set; } = "mongodb://mongo:27017";
    public string Database { get; set; } = "audit_db";
    public string Collection { get; set; } = "audit_entries";
    /// <summary>Retención (índice TTL). Ajustar a la política de cumplimiento (p. ej. 5 años en finanzas).</summary>
    public int RetentionDays { get; set; } = 365;
}

/// <summary>Modelo de persistencia (separado del dominio para no contaminarlo con atributos de Mongo).</summary>
internal sealed class AuditDocument
{
    [BsonId] public string Id { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)] public DateTime OccurredAt { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)] public DateTime RecordedAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    public string? ActorEmail { get; set; }
    public string? SubjectId { get; set; }
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
    public bool Success { get; set; }
    public Dictionary<string, string> Details { get; set; } = new();

    public static AuditDocument From(AuditEntry e) => new()
    {
        Id = e.Id.ToString(),
        EventType = e.EventType,
        Category = e.Category,
        OccurredAt = e.OccurredAt.UtcDateTime,
        RecordedAt = e.RecordedAt.UtcDateTime,
        Source = e.Source,
        ActorId = e.ActorId,
        ActorEmail = e.ActorEmail,
        SubjectId = e.SubjectId,
        IpAddress = e.IpAddress,
        CorrelationId = e.CorrelationId,
        Success = e.Success,
        Details = e.Details
    };

    public AuditEntry ToDomain() => AuditEntry.Create(
        Guid.Parse(Id), EventType, new DateTimeOffset(OccurredAt, TimeSpan.Zero), new DateTimeOffset(RecordedAt, TimeSpan.Zero),
        Source, ActorId, ActorEmail, SubjectId, IpAddress, CorrelationId, Success, Details);
}

public sealed class MongoAuditRepository : IAuditRepository
{
    private readonly IMongoCollection<AuditDocument> _collection;

    public MongoAuditRepository(IMongoClient client, IOptions<MongoOptions> options)
    {
        _collection = client.GetDatabase(options.Value.Database).GetCollection<AuditDocument>(options.Value.Collection);
    }

    internal IMongoCollection<AuditDocument> Collection => _collection;

    public async Task<bool> InsertIfNotExistsAsync(AuditEntry entry, CancellationToken ct)
    {
        try
        {
            await _collection.InsertOneAsync(AuditDocument.From(entry), cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false; // idempotencia: el _id es el Id del evento
        }
    }

    public async Task<(IReadOnlyList<AuditEntry> Items, long Total)> SearchAsync(AuditQuery query, CancellationToken ct)
    {
        var filter = BuildFilter(query);
        var total = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs = await _collection.Find(filter)
            .SortByDescending(d => d.OccurredAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Limit(query.PageSize)
            .ToListAsync(ct);
        return (docs.Select(d => d.ToDomain()).ToList(), total);
    }

    public async Task<IReadOnlyDictionary<string, long>> CountByEventTypeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var filter = Builders<AuditDocument>.Filter.Gte(d => d.OccurredAt, from.UtcDateTime)
                     & Builders<AuditDocument>.Filter.Lte(d => d.OccurredAt, to.UtcDateTime);
        var groups = await _collection.Aggregate()
            .Match(filter)
            .Group(d => d.EventType, g => new { EventType = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return groups.OrderByDescending(g => g.Count).ToDictionary(g => g.EventType, g => (long)g.Count);
    }

    private static FilterDefinition<AuditDocument> BuildFilter(AuditQuery q)
    {
        var f = Builders<AuditDocument>.Filter;
        var filters = new List<FilterDefinition<AuditDocument>>();
        if (q.EventType is not null) filters.Add(f.Eq(d => d.EventType, q.EventType));
        if (q.Category is not null) filters.Add(f.Eq(d => d.Category, q.Category));
        if (q.ActorId is not null) filters.Add(f.Eq(d => d.ActorId, q.ActorId));
        if (q.SubjectId is not null) filters.Add(f.Eq(d => d.SubjectId, q.SubjectId));
        if (q.Success is not null) filters.Add(f.Eq(d => d.Success, q.Success.Value));
        if (q.From is not null) filters.Add(f.Gte(d => d.OccurredAt, q.From.Value.UtcDateTime));
        if (q.To is not null) filters.Add(f.Lte(d => d.OccurredAt, q.To.Value.UtcDateTime));
        return filters.Count == 0 ? f.Empty : f.And(filters);
    }

    /// <summary>Índices para los patrones de consulta + TTL de retención.</summary>
    public async Task EnsureIndexesAsync(int retentionDays, CancellationToken ct)
    {
        var keys = Builders<AuditDocument>.IndexKeys;
        await _collection.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<AuditDocument>(keys.Descending(d => d.OccurredAt)),
            new CreateIndexModel<AuditDocument>(keys.Ascending(d => d.EventType).Descending(d => d.OccurredAt)),
            new CreateIndexModel<AuditDocument>(keys.Ascending(d => d.SubjectId).Descending(d => d.OccurredAt)),
            new CreateIndexModel<AuditDocument>(keys.Ascending(d => d.ActorId).Descending(d => d.OccurredAt)),
            new CreateIndexModel<AuditDocument>(keys.Ascending(d => d.RecordedAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(retentionDays), Name = "ttl_retention" })
        }, ct);
    }
}

internal sealed class MongoHealthCheck(IMongoClient client, IOptions<MongoOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.GetDatabase(options.Value.Database).RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB no responde", ex);
        }
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddAuditInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.SectionName));
        services.AddSingleton<IMongoClient>(sp => new MongoClient(sp.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString));
        services.AddSingleton<MongoAuditRepository>();
        services.AddSingleton<IAuditRepository>(sp => sp.GetRequiredService<MongoAuditRepository>());
        services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongodb");
        services.AddEventConsumer<AuditEventHandler>("audit-service.all-events");
        return services;
    }

    public static async Task InitializeAuditStoreAsync(this IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AuditInit");
        var repository = services.GetRequiredService<MongoAuditRepository>();
        var options = services.GetRequiredService<IOptions<MongoOptions>>().Value;
        await StartupRetry.RunAsync(() => repository.EnsureIndexesAsync(options.RetentionDays, CancellationToken.None), logger, "Creación de índices MongoDB");
    }
}
