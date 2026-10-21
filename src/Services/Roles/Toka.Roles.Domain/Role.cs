using System.Text.RegularExpressions;

namespace Toka.Roles.Domain;

public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Aggregate Root: un rol agrupa permisos con formato "recurso:acción".
/// Invariantes: nombre único y válido, permisos bien formados y sin duplicados,
/// los roles de sistema no se eliminan ni se renombran.
/// </summary>
public sealed partial class Role
{
    private Role() { } // EF Core

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
    public List<string> Permissions { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }

    public static Role Create(Guid id, string name, string? description, IEnumerable<string> permissions, bool isSystem, DateTimeOffset now)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (!NamePattern().IsMatch(trimmed))
            throw new DomainException("role.name_invalid", "El nombre del rol debe tener 2-40 caracteres alfanuméricos, guion o guion bajo, iniciando con letra.");
        if (description is { Length: > 200 })
            throw new DomainException("role.description_invalid", "La descripción excede 200 caracteres.");

        var role = new Role
        {
            Id = id == Guid.Empty ? Guid.NewGuid() : id,
            Name = trimmed,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            IsSystem = isSystem,
            CreatedAt = now
        };
        role.ReplacePermissions(permissions);
        return role;
    }

    public void ReplacePermissions(IEnumerable<string> permissions)
    {
        var normalized = permissions.Select(p => p?.Trim().ToLowerInvariant() ?? string.Empty).Distinct().ToList();
        var invalid = normalized.Where(p => !PermissionPattern().IsMatch(p)).ToList();
        if (invalid.Count > 0)
            throw new DomainException("role.permission_invalid", $"Permisos con formato inválido: {string.Join(", ", invalid)}. Formato: recurso:acción");
        if (normalized.Count > 50)
            throw new DomainException("role.permission_limit", "Un rol no puede tener más de 50 permisos.");
        Permissions = normalized.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    public void EnsureCanBeDeleted()
    {
        if (IsSystem) throw new DomainException("role.system", "Los roles de sistema no pueden eliminarse.");
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{1,39}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[a-z][a-z0-9-]{1,30}:[a-z][a-z0-9-]{1,30}$")]
    private static partial Regex PermissionPattern();
}

/// <summary>Asignación usuario ↔ rol (entidad propia: guarda quién y cuándo la otorgó).</summary>
public sealed class UserRoleAssignment
{
    private UserRoleAssignment() { }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Role? Role { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public string? AssignedBy { get; private set; }

    public static UserRoleAssignment Create(Guid userId, Role role, string? assignedBy, DateTimeOffset now)
    {
        if (userId == Guid.Empty) throw new DomainException("assignment.user", "El usuario es obligatorio.");
        return new UserRoleAssignment { UserId = userId, RoleId = role.Id, Role = role, AssignedAt = now, AssignedBy = assignedBy };
    }
}
