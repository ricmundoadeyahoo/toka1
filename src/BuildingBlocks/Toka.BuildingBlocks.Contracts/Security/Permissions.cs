namespace Toka.BuildingBlocks.Contracts.Security;

/// <summary>
/// Permisos (RBAC granular). Los roles agrupan permisos; los tokens transportan permisos
/// para que cada microservicio autorice sin llamar a Role Service en cada petición.
/// </summary>
public static class Permissions
{
    public const string UsersRead = "users:read";
    public const string UsersWrite = "users:write";
    public const string RolesRead = "roles:read";
    public const string RolesManage = "roles:manage";
    public const string AuditRead = "audit:read";
    public const string AiUse = "ai:use";
    public const string AiAdmin = "ai:admin";

    public static readonly IReadOnlyList<string> All =
        [UsersRead, UsersWrite, RolesRead, RolesManage, AuditRead, AiUse, AiAdmin];
}

public static class SystemRoles
{
    public const string Admin = "Admin";
    public const string Auditor = "Auditor";
    public const string User = "User";
}

/// <summary>Scopes OAuth2 para comunicación servicio-a-servicio (client_credentials).</summary>
public static class Scopes
{
    public const string RolesRead = "roles.read";
    public const string UsersRead = "users.read";
}

public static class TokaClaimTypes
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string Permission = "permission";
    public const string Scope = "scope";
    public const string ClientId = "client_id";
    public const string JwtId = "jti";
}

/// <summary>Datos semilla compartidos: el administrador inicial existe en Auth, User y Role.</summary>
public static class SeedData
{
    public static readonly Guid AdminUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public const string AdminEmail = "admin@toka.local";
    public const string AdminFullName = "Administrador Toka";
}
