using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Toka.Audit.Domain;
using Toka.BuildingBlocks.Contracts.Events;
using Toka.BuildingBlocks.Contracts.Results;
using Toka.BuildingBlocks.Contracts.Validation;

namespace Toka.Audit.Application;

public sealed record AuditQuery(
    string? EventType, string? Category, string? ActorId, string? SubjectId, bool? Success,
    DateTimeOffset? From, DateTimeOffset? To, int Page, int PageSize);

public sealed record AuditSummaryDto(
    DateTimeOffset From, DateTimeOffset To, long Total, long Failures,
    IReadOnlyDictionary<string, long> ByEventType, IReadOnlyList<AuditEntryDto> RecentFailures);

public sealed record AuditEntryDto(
    Guid Id, string EventType, string Category, DateTimeOffset OccurredAt, string Source,
    string? ActorId, string? ActorEmail, string? SubjectId, string? IpAddress, string? CorrelationId,
    bool Success, IReadOnlyDictionary<string, string> Details)
{
    public static AuditEntryDto From(AuditEntry e) => new(
        e.Id, e.EventType, e.Category, e.OccurredAt, e.Source, e.ActorId, e.ActorEmail,
        e.SubjectId, e.IpAddress, e.CorrelationId, e.Success, e.Details);
}

public interface IAuditRepository
{
    /// <returns>false si ya existía (evento duplicado).</returns>
    Task<bool> InsertIfNotExistsAsync(AuditEntry entry, CancellationToken ct);
    Task<(IReadOnlyList<AuditEntry> Items, long Total)> SearchAsync(AuditQuery query, CancellationToken ct);
    Task<IReadOnlyDictionary<string, long>> CountByEventTypeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed class AuditUseCases(IAuditRepository repository, TimeProvider timeProvider)
{
    public const int MaxSummaryHours = 24 * 30;

    public async Task<bool> RecordAsync(IntegrationEvent evt, CancellationToken ct)
    {
        var entry = AuditEntry.Create(
            evt.Id, evt.EventType, evt.OccurredAt, timeProvider.GetUtcNow(), evt.Source,
            evt.ActorId, evt.ActorEmail, evt.SubjectId, evt.IpAddress, evt.CorrelationId, evt.Success, evt.Data);
        return await repository.InsertIfNotExistsAsync(entry, ct);
    }

    public async Task<Result<PagedResult<AuditEntryDto>>> SearchAsync(
        string? eventType, string? category, string? actorId, string? subjectId, bool? success,
        DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize, CancellationToken ct)
    {
        foreach (var value in new[] { eventType, category, actorId, subjectId })
        {
            if (value is { Length: > 100 } || !TextInput.IsSafe(value))
                return Error.Validation("audit.filter_invalid", "Alguno de los filtros no es válido.");
        }
        if (from is not null && to is not null && from > to)
            return Error.Validation("audit.range_invalid", "La fecha 'from' debe ser anterior a 'to'.");

        var (p, size) = Paging.Normalize(page, pageSize, 25);
        var (items, total) = await repository.SearchAsync(new AuditQuery(
            TextInput.Normalize(eventType), TextInput.Normalize(category), TextInput.Normalize(actorId),
            TextInput.Normalize(subjectId), success, from, to, p, size), ct);
        return Result<PagedResult<AuditEntryDto>>.Success(
            new PagedResult<AuditEntryDto>(items.Select(AuditEntryDto.From).ToList(), p, size, total));
    }

    public async Task<Result<AuditSummaryDto>> SummaryAsync(int? hours, CancellationToken ct)
    {
        var h = hours ?? 24;
        if (h < 1 || h > MaxSummaryHours)
            return Error.Validation("audit.hours_invalid", $"hours debe estar entre 1 y {MaxSummaryHours}.");

        var to = timeProvider.GetUtcNow();
        var from = to.AddHours(-h);
        var byType = await repository.CountByEventTypeAsync(from, to, ct);
        var (failures, failureCount) = await repository.SearchAsync(new AuditQuery(null, null, null, null, false, from, to, 1, 10), ct);
        return Result<AuditSummaryDto>.Success(new AuditSummaryDto(
            from, to, byType.Values.Sum(), failureCount, byType, failures.Select(AuditEntryDto.From).ToList()));
    }
}

/// <summary>Suscrito a TODOS los eventos ("#"): la auditoría es un consumidor pasivo y desacoplado.</summary>
public sealed class AuditEventHandler(AuditUseCases useCases, ILogger<AuditEventHandler> logger) : IIntegrationEventHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } = ["#"];

    public async Task HandleAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var inserted = await useCases.RecordAsync(integrationEvent, cancellationToken);
        if (!inserted) logger.LogInformation("Evento {EventId} ya auditado (duplicado ignorado)", integrationEvent.Id);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddAuditApplication(this IServiceCollection services)
    {
        services.AddScoped<AuditUseCases>();
        return services;
    }
}
