using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Toka.Auth.Application;
using Toka.BuildingBlocks.Contracts.Caching;

namespace Toka.Auth.Infrastructure.Security;

/// <summary>
/// Refresh tokens en Redis con TTL. Se guarda el SHA-256 del token (nunca el token en claro),
/// de modo que un volcado de Redis no permite secuestrar sesiones.
/// </summary>
public sealed class RedisRefreshTokenStore(ICacheService cache, IDistributedCache rawCache, TimeProvider timeProvider) : IRefreshTokenStore
{
    public Task StoreAsync(string refreshToken, RefreshTokenEntry entry, CancellationToken ct)
    {
        var ttl = entry.ExpiresAt - timeProvider.GetUtcNow();
        return cache.SetAsync(CacheKeys.RefreshToken(Hash(refreshToken)), entry, ttl > TimeSpan.Zero ? ttl : TimeSpan.FromSeconds(1), ct);
    }

    public async Task<RefreshTokenEntry?> ConsumeAsync(string refreshToken, CancellationToken ct)
    {
        var key = CacheKeys.RefreshToken(Hash(refreshToken));
        var entry = await cache.GetAsync<RefreshTokenEntry>(key, ct);
        if (entry is not null) await rawCache.RemoveAsync(key, ct);
        return entry;
    }

    public Task RevokeAsync(string refreshToken, CancellationToken ct) =>
        cache.RemoveAsync(CacheKeys.RefreshToken(Hash(refreshToken)), ct);

    internal static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>Lista de revocación de access tokens (jti) consultada por todos los servicios.</summary>
public sealed class RedisTokenRevocationStore(ICacheService cache, TimeProvider timeProvider) : ITokenRevocationStore
{
    public Task RevokeAccessTokenAsync(string jwtId, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var ttl = expiresAt - timeProvider.GetUtcNow() + TimeSpan.FromMinutes(1);
        return cache.SetAsync(CacheKeys.RevokedToken(jwtId), "1", ttl > TimeSpan.Zero ? ttl : TimeSpan.FromMinutes(1), ct);
    }
}
