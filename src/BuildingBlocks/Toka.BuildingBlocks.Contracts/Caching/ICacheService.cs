namespace Toka.BuildingBlocks.Contracts.Caching;

/// <summary>Puerto de caché (cache-aside). Implementado sobre Redis.</summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Claves de caché compartidas entre servicios (p. ej. revocación de tokens).</summary>
public static class CacheKeys
{
    public static string RevokedToken(string jti) => $"auth:revoked:{jti}";
    public static string RefreshToken(string tokenHash) => $"auth:refresh:{tokenHash}";
    public static string UserRoles(Guid userId) => $"roles:user:{userId}";
    public static string UserProfile(Guid userId) => $"users:profile:{userId}";
    public static string LastKnownRoles(Guid userId) => $"auth:last-roles:{userId}";
}
