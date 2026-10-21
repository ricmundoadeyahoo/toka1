using Toka.Auth.Domain;

namespace Toka.Auth.Application;

// ---------- Puertos de salida (implementados en Infrastructure) ----------

public interface ICredentialRepository
{
    Task<UserCredential?> GetByEmailAsync(string normalizedEmail, CancellationToken ct);
    Task<UserCredential?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsByEmailAsync(string normalizedEmail, CancellationToken ct);
    Task AddAsync(UserCredential credential, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public sealed record AccessTokenRequest(
    string Subject,
    string? Email,
    string? Name,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<string> Scopes,
    string? ClientId = null);

public sealed record IssuedAccessToken(string Token, string JwtId, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    IssuedAccessToken CreateAccessToken(AccessTokenRequest request);
    string CreateRefreshToken();
}

public sealed record RefreshTokenEntry(Guid UserId, DateTimeOffset ExpiresAt);

public interface IRefreshTokenStore
{
    Task StoreAsync(string refreshToken, RefreshTokenEntry entry, CancellationToken ct);
    /// <summary>Lee y elimina (uso único → rotación de refresh tokens).</summary>
    Task<RefreshTokenEntry?> ConsumeAsync(string refreshToken, CancellationToken ct);
    Task RevokeAsync(string refreshToken, CancellationToken ct);
}

public interface ITokenRevocationStore
{
    Task RevokeAccessTokenAsync(string jwtId, DateTimeOffset expiresAt, CancellationToken ct);
}

public sealed record UserAuthorization(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

/// <summary>Cliente REST (síncrono) hacia Role Service.</summary>
public interface IRoleServiceClient
{
    /// <returns>null si Role Service no está disponible y no hay valor de respaldo.</returns>
    Task<UserAuthorization?> GetUserAuthorizationAsync(Guid userId, CancellationToken ct);
}

public sealed record OAuthClient(string ClientId, string ClientSecret, IReadOnlyList<string> AllowedScopes);

public interface IClientRegistry
{
    OAuthClient? Find(string clientId);
}

// ---------- Configuración ----------

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

// ---------- DTOs ----------

public sealed record RegisterCommand(string Email, string Password, string FullName, string? Department, string? IpAddress);
public sealed record RegisterResponse(Guid UserId, string Email);

public sealed record LoginCommand(string Email, string Password, string? IpAddress);
public sealed record RefreshCommand(string RefreshToken, string? IpAddress);
public sealed record LogoutCommand(Guid UserId, string? Email, string JwtId, DateTimeOffset AccessTokenExpiresAt, string? RefreshToken);
public sealed record ClientCredentialsCommand(string ClientId, string ClientSecret, string? Scope);

public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string? RefreshToken,
    string? Scope,
    AuthenticatedUser? User);

public sealed record AuthenticatedUser(Guid Id, string Email, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);
