using Microsoft.Extensions.Logging.Abstractions;
using Toka.Audit.Application;
using Toka.Audit.Domain;
using Toka.BuildingBlocks.Contracts.Events;
using Toka.BuildingBlocks.Contracts.Results;
using Toka.Tests.Shared;

namespace Toka.Audit.UnitTests;

internal sealed class InMemoryAuditRepository : IAuditRepository
{
    public Dictionary<Guid, AuditEntry> Items { get; } = new();

    public Task<bool> InsertIfNotExistsAsync(AuditEntry entry, CancellationToken ct) => Task.FromResult(Items.TryAdd(entry.Id, entry));

    public Task<(IReadOnlyList<AuditEntry> Items, long Total)> SearchAsync(AuditQuery q, CancellationToken ct)
    {
        var query = Items.Values.AsEnumerable();
        if (q.EventType is not null) query = query.Where(e => e.EventType == q.EventType);
        if (q.Category is not null) query = query.Where(e => e.Category == q.Category);
        if (q.ActorId is not null) query = query.Where(e => e.ActorId == q.ActorId);
        if (q.SubjectId is not null) query = query.Where(e => e.SubjectId == q.SubjectId);
        if (q.Success is not null) query = query.Where(e => e.Success == q.Success);
        if (q.From is not null) query = query.Where(e => e.OccurredAt >= q.From);
        if (q.To is not null) query = query.Where(e => e.OccurredAt <= q.To);
        var list = query.OrderByDescending(e => e.OccurredAt).ToList();
        return Task.FromResult<(IReadOnlyList<AuditEntry>, long)>((list.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList(), list.Count));
    }

    public Task<IReadOnlyDictionary<string, long>> CountByEventTypeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, long>>(Items.Values
            .Where(e => e.OccurredAt >= from && e.OccurredAt <= to)
            .GroupBy(e => e.EventType).ToDictionary(g => g.Key, g => (long)g.Count()));
}

public class AuditEntryTests
{
    [Fact]
    public void Create_deriva_categoria_y_sanitiza_detalles()
    {
        var details = new Dictionary<string, string>
        {
            ["reason"] = "bad_password",
            ["password"] = "Secreta#1",
            ["refreshToken"] = "abc",
            ["largo"] = new string('x', 600)
        };
        var entry = AuditEntry.Create(Guid.NewGuid(), "identity.login.failed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            "auth-service", null, "a@toka.com", null, "1.1.1.1", "corr", false, details);

        Assert.Equal("identity", entry.Category);
        Assert.Equal("***", entry.Details["password"]);
        Assert.Equal("***", entry.Details["refreshToken"]);
        Assert.Equal(AuditEntry.MaxDetailValueLength + 1, entry.Details["largo"].Length);
        Assert.Equal("bad_password", entry.Details["reason"]);
        Assert.False(entry.Success);
    }

    [Fact]
    public void Create_limita_numero_de_detalles_y_acepta_null()
    {
        var many = Enumerable.Range(0, 40).ToDictionary(i => $"k{i}", i => "v");
        Assert.Equal(AuditEntry.MaxDetails, Create(many).Details.Count);
        Assert.Empty(Create(null).Details);
    }

    [Fact]
    public void Create_valida_obligatorios()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => AuditEntry.Create(Guid.Empty, "t", now, now, "s", null, null, null, null, null, true, null));
        Assert.Throws<ArgumentException>(() => AuditEntry.Create(Guid.NewGuid(), " ", now, now, "s", null, null, null, null, null, true, null));
        Assert.Throws<ArgumentException>(() => AuditEntry.Create(Guid.NewGuid(), "t", now, now, "", null, null, null, null, null, true, null));
    }

    private static AuditEntry Create(IReadOnlyDictionary<string, string>? details) =>
        AuditEntry.Create(Guid.NewGuid(), "x.y", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "s", null, null, null, null, null, true, details);
}

public class AuditUseCaseTests
{
    private readonly InMemoryAuditRepository _repo = new();
    private readonly FixedTimeProvider _clock = FixedTimeProvider.Default();
    private AuditUseCases UseCases => new(_repo, _clock);

    private IntegrationEvent Event(string type, bool success = true, string? subject = null, int minutesAgo = 5) => new()
    {
        EventType = type, Source = "test", Success = success, SubjectId = subject,
        OccurredAt = _clock.GetUtcNow().AddMinutes(-minutesAgo), Data = new() { ["k"] = "v" }
    };

    [Fact]
    public async Task Handler_registra_y_es_idempotente()
    {
        var handler = new AuditEventHandler(UseCases, NullLogger<AuditEventHandler>.Instance);
        var evt = Event(EventTypes.LoginSucceeded);
        await handler.HandleAsync(evt, default);
        await handler.HandleAsync(evt, default);
        Assert.Single(_repo.Items);
        Assert.Equal(new[] { "#" }, handler.EventTypes.ToArray());
        Assert.Equal(_clock.GetUtcNow(), _repo.Items[evt.Id].RecordedAt);
    }

    [Fact]
    public async Task Search_filtra_y_pagina()
    {
        var subject = Guid.NewGuid().ToString();
        await UseCases.RecordAsync(Event(EventTypes.LoginFailed, false, subject), default);
        await UseCases.RecordAsync(Event(EventTypes.LoginSucceeded, true, subject), default);
        await UseCases.RecordAsync(Event(EventTypes.RoleAssigned), default);

        var failed = await UseCases.SearchAsync(null, null, null, subject, false, null, null, 1, 10, default);
        Assert.Single(failed.Value.Items);
        Assert.Equal(EventTypes.LoginFailed, failed.Value.Items[0].EventType);

        var identity = await UseCases.SearchAsync(null, "identity", null, null, null, null, null, null, null, default);
        Assert.Equal(2, identity.Value.TotalCount);
        Assert.Equal(25, identity.Value.PageSize);
    }

    [Fact]
    public async Task Search_valida_filtros_y_rango()
    {
        Assert.Equal(ErrorType.Validation, (await UseCases.SearchAsync("<x>", null, null, null, null, null, null, 1, 10, default)).Error!.Type);
        Assert.Equal(ErrorType.Validation, (await UseCases.SearchAsync(new string('a', 101), null, null, null, null, null, null, 1, 10, default)).Error!.Type);
        var now = _clock.GetUtcNow();
        Assert.Equal("audit.range_invalid", (await UseCases.SearchAsync(null, null, null, null, null, now, now.AddDays(-1), 1, 10, default)).Error!.Code);
    }

    [Fact]
    public async Task Summary_agrega_por_tipo_y_lista_fallos()
    {
        await UseCases.RecordAsync(Event(EventTypes.LoginFailed, false), default);
        await UseCases.RecordAsync(Event(EventTypes.LoginFailed, false), default);
        await UseCases.RecordAsync(Event(EventTypes.LoginSucceeded), default);
        await UseCases.RecordAsync(Event(EventTypes.LoginSucceeded, minutesAgo: 60 * 48), default); // fuera de ventana

        var summary = await UseCases.SummaryAsync(24, default);

        Assert.True(summary.IsSuccess);
        Assert.Equal(3, summary.Value.Total);
        Assert.Equal(2, summary.Value.Failures);
        Assert.Equal(2, summary.Value.ByEventType[EventTypes.LoginFailed]);
        Assert.Equal(2, summary.Value.RecentFailures.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_000)]
    public async Task Summary_valida_ventana(int hours) =>
        Assert.Equal(ErrorType.Validation, (await UseCases.SummaryAsync(hours, default)).Error!.Type);

    [Fact]
    public void Dto_refleja_entrada()
    {
        var entry = AuditEntry.Create(Guid.NewGuid(), "users.user.updated", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "user-service",
            "actor", "a@toka.com", "subject", "ip", "corr", true, null);
        var dto = AuditEntryDto.From(entry);
        Assert.Equal("users", dto.Category);
        Assert.Equal("corr", dto.CorrelationId);
    }
}
