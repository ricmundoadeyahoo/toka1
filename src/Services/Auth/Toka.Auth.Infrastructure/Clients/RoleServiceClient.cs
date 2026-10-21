using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Toka.Auth.Application;
using Toka.BuildingBlocks.Contracts.Caching;
using Toka.BuildingBlocks.Contracts.Security;

namespace Toka.Auth.Infrastructure.Clients;

/// <summary>
/// Comunicación SÍNCRONA (REST) Auth → Role Service para obtener roles/permisos al emitir el token.
/// - Se autentica con un token de servicio (client_credentials interno, scope roles.read).
/// - HttpClient con retry + circuit breaker + timeout (Microsoft.Extensions.Http.Resilience).
/// - Fallback: último valor conocido en Redis (degradación elegante si Role Service cae).
/// </summary>
public sealed class RoleServiceClient(
    HttpClient httpClient,
    ITokenService tokenService,
    ICacheService cache,
    ILogger<RoleServiceClient> logger) : IRoleServiceClient
{
    private sealed record UserRolesDto(Guid UserId, List<string> Roles, List<string> Permissions);

    public async Task<UserAuthorization?> GetUserAuthorizationAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/roles/users/{userId}");
            var serviceToken = tokenService.CreateAccessToken(new AccessTokenRequest(
                "auth-service", null, null, [], [], [Scopes.RolesRead], "auth-service"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken.Token);

            using var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<UserRolesDto>(ct)
                      ?? throw new InvalidOperationException("Respuesta vacía de Role Service");

            var result = new UserAuthorization(dto.Roles, dto.Permissions);
            await cache.SetAsync(CacheKeys.LastKnownRoles(userId), result, TimeSpan.FromHours(12), ct);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var fallback = await cache.GetAsync<UserAuthorization>(CacheKeys.LastKnownRoles(userId), ct);
            if (fallback is not null)
            {
                logger.LogWarning(ex, "Role Service no disponible; usando roles en caché para {UserId}", userId);
                return fallback;
            }
            logger.LogError(ex, "Role Service no disponible y sin caché para {UserId}", userId);
            return null;
        }
    }
}
