using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Toka.BuildingBlocks.Contracts.Caching;

namespace Toka.BuildingBlocks.Infrastructure.Caching;

/// <summary>
/// Caché distribuida sobre Redis. Es tolerante a fallos: si Redis no responde, se registra un warning
/// y la operación continúa contra la fuente de verdad (la caché nunca debe tumbar el servicio).
/// </summary>
public sealed class DistributedCacheService(IDistributedCache cache, ILogger<DistributedCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await cache.GetStringAsync(key, cancellationToken);
            return json is null ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache GET falló para {CacheKey}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await cache.SetStringAsync(key, json, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache SET falló para {CacheKey}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache REMOVE falló para {CacheKey}", key);
        }
    }
}

public static class CachingExtensions
{
    public static IServiceCollection AddTokaRedisCache(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Redis") ?? "redis:6379";
        services.AddStackExchangeRedisCache(options =>
        {
            // abortConnect=false: el servicio arranca aunque Redis aún no esté listo.
            options.Configuration = connection.Contains("abortConnect", StringComparison.OrdinalIgnoreCase)
                ? connection
                : connection + ",abortConnect=false,connectTimeout=3000,syncTimeout=2000";
            options.InstanceName = "toka:";
        });
        services.AddSingleton<ICacheService, DistributedCacheService>();
        return services;
    }
}
