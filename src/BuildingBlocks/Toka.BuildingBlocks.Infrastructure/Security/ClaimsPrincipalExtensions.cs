using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Toka.BuildingBlocks.Contracts.Security;

namespace Toka.BuildingBlocks.Infrastructure.Security;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(TokaClaimTypes.Subject)?.Value, out var id) ? id : null;

    public static string? GetEmail(this ClaimsPrincipal principal) =>
        principal.FindFirst(TokaClaimTypes.Email)?.Value;

    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.Claims.Any(c => c.Type == TokaClaimTypes.Permission && c.Value == permission);

    public static bool HasScope(this ClaimsPrincipal principal, string scope) =>
        principal.Claims
            .Where(c => c.Type == TokaClaimTypes.Scope)
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope);

    public static IReadOnlyList<string> GetRoles(this ClaimsPrincipal principal) =>
        principal.Claims.Where(c => c.Type == TokaClaimTypes.Role).Select(c => c.Value).ToList();

    /// <summary>Token bearer de la petición actual (para token relay hacia otros servicios).</summary>
    public static string? GetBearerToken(this HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
    }

    public static string? GetClientIp(this HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString();
}
