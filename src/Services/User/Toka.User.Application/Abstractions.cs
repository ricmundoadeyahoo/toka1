using Toka.User.Domain;

namespace Toka.User.Application;

public interface IUserRepository
{
    Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);
    Task<(IReadOnlyList<UserProfile> Items, long Total)> SearchAsync(UserSearchCriteria criteria, CancellationToken ct);
    Task<UserStatsDto> GetStatsAsync(CancellationToken ct);
    Task AddAsync(UserProfile profile, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public sealed record UserSearchCriteria(string? Search, UserStatus? Status, string? Department, int Page, int PageSize);

public sealed record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string? Department,
    string? JobTitle,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static UserDto From(UserProfile p) =>
        new(p.Id, p.Email, p.FullName, p.Department, p.JobTitle, p.Status.ToString(), p.CreatedAt, p.UpdatedAt);
}

public sealed record UserStatsDto(long Total, long Active, long Deactivated, IReadOnlyDictionary<string, long> ByDepartment);

/// <summary>Quién ejecuta la acción (para autorización a nivel de recurso y auditoría).</summary>
public sealed record Actor(Guid? UserId, string? Email, bool CanWriteUsers, bool CanReadUsers);

public sealed record UpdateUserCommand(Guid UserId, string FullName, string? Department, string? JobTitle, Actor Actor);
