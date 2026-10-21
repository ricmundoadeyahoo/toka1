using Toka.BuildingBlocks.Contracts.Caching;
using Toka.BuildingBlocks.Contracts.Results;
using Toka.BuildingBlocks.Contracts.Validation;
using Toka.User.Domain;

namespace Toka.User.Application;

/// <summary>Lado de lectura (CQRS ligero): consultas con caché cache-aside en Redis.</summary>
public sealed class UserQueries(IUserRepository repository, ICacheService cache)
{
    internal static readonly TimeSpan ProfileTtl = TimeSpan.FromMinutes(10);

    public async Task<Result<UserDto>> GetByIdAsync(Guid id, Actor actor, CancellationToken ct)
    {
        if (!actor.CanReadUsers && actor.UserId != id)
            return Error.Forbidden("users.forbidden", "No tienes permiso para consultar este usuario.");

        var cached = await cache.GetAsync<UserDto>(CacheKeys.UserProfile(id), ct);
        if (cached is not null) return Result<UserDto>.Success(cached);

        var profile = await repository.GetByIdAsync(id, ct);
        if (profile is null) return Error.NotFound("users.not_found", "Usuario no encontrado.");

        var dto = UserDto.From(profile);
        await cache.SetAsync(CacheKeys.UserProfile(id), dto, ProfileTtl, ct);
        return Result<UserDto>.Success(dto);
    }

    public async Task<Result<PagedResult<UserDto>>> SearchAsync(string? search, string? status, string? department, int? page, int? pageSize, CancellationToken ct)
    {
        var normalizedSearch = TextInput.Normalize(search);
        if (normalizedSearch is { Length: > 100 } || !TextInput.IsSafe(normalizedSearch))
            return Error.Validation("users.search_invalid", "El término de búsqueda no es válido.");

        UserStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<UserStatus>(status, ignoreCase: true, out var s) || !Enum.IsDefined(s))
                return Error.Validation("users.status_invalid", "Estado inválido. Valores: Active, Deactivated.");
            parsedStatus = s;
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var (items, total) = await repository.SearchAsync(
            new UserSearchCriteria(normalizedSearch, parsedStatus, TextInput.Normalize(department), p, size), ct);

        return Result<PagedResult<UserDto>>.Success(
            new PagedResult<UserDto>(items.Select(UserDto.From).ToList(), p, size, total));
    }

    public Task<UserStatsDto> GetStatsAsync(CancellationToken ct) => repository.GetStatsAsync(ct);
}
