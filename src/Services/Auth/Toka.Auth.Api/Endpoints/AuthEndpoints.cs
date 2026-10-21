using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Toka.Auth.Application;
using Toka.Auth.Application.UseCases;
using Toka.Auth.Infrastructure.Security;
using Toka.BuildingBlocks.Contracts.Results;
using Toka.BuildingBlocks.Contracts.Security;
using Toka.BuildingBlocks.Infrastructure.Http;
using Toka.BuildingBlocks.Infrastructure.Security;

namespace Toka.Auth.Api.Endpoints;

public sealed record RegisterRequest(string Email, string Password, string FullName, string? Department);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string? RefreshToken);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest body, RegisterUseCase useCase, HttpContext http, CancellationToken ct) =>
            {
                var result = await useCase.ExecuteAsync(
                    new RegisterCommand(body.Email, body.Password, body.FullName, body.Department, http.GetClientIp()), ct);
                return result.ToHttpResult(r => Results.Created($"/api/users/{r.UserId}", r));
            })
            .RequireRateLimiting("auth");

        group.MapPost("/login", async (LoginRequest body, LoginUseCase useCase, HttpContext http, CancellationToken ct) =>
                (await useCase.ExecuteAsync(new LoginCommand(body.Email, body.Password, http.GetClientIp()), ct)).ToHttpResult())
            .RequireRateLimiting("auth");

        group.MapPost("/refresh", async (RefreshRequest body, RefreshTokenUseCase useCase, HttpContext http, CancellationToken ct) =>
                (await useCase.ExecuteAsync(new RefreshCommand(body.RefreshToken, http.GetClientIp()), ct)).ToHttpResult())
            .RequireRateLimiting("auth");

        group.MapPost("/logout", async ([FromBody] LogoutRequest? body, ClaimsPrincipal user, LogoutUseCase useCase, CancellationToken ct) =>
            {
                var userId = user.GetUserId();
                var jti = user.FindFirst(TokaClaimTypes.JwtId)?.Value;
                var exp = long.TryParse(user.FindFirst("exp")?.Value, out var unix)
                    ? DateTimeOffset.FromUnixTimeSeconds(unix)
                    : DateTimeOffset.UtcNow.AddHours(1);
                if (userId is null || jti is null) return Results.Unauthorized();
                return (await useCase.ExecuteAsync(new LogoutCommand(userId.Value, user.GetEmail(), jti, exp, body?.RefreshToken), ct)).ToHttpResult();
            })
            .RequireAuthorization();

        group.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
            {
                id = user.GetUserId(),
                email = user.GetEmail(),
                roles = user.GetRoles(),
                permissions = user.Claims.Where(c => c.Type == TokaClaimTypes.Permission).Select(c => c.Value)
            }))
            .RequireAuthorization();

        return app;
    }

    /// <summary>
    /// Endpoints OAuth2/OIDC: token endpoint (RFC 6749), discovery (OIDC Discovery 1.0) y JWKS (RFC 7517).
    /// </summary>
    public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/connect/token", async (HttpContext http, LoginUseCase login, RefreshTokenUseCase refresh,
                ClientCredentialsUseCase clientCredentials, CancellationToken ct) =>
            {
                if (!http.Request.HasFormContentType)
                    return OAuthError("invalid_request", "Se esperaba application/x-www-form-urlencoded.");

                var form = await http.Request.ReadFormAsync(ct);
                var grantType = form["grant_type"].ToString();
                Result<TokenResponse> result = grantType switch
                {
                    "password" => await login.ExecuteAsync(new LoginCommand(form["username"].ToString(), form["password"].ToString(), http.GetClientIp()), ct),
                    "refresh_token" => await refresh.ExecuteAsync(new RefreshCommand(form["refresh_token"].ToString(), http.GetClientIp()), ct),
                    "client_credentials" => await clientCredentials.ExecuteAsync(new ClientCredentialsCommand(
                        form["client_id"].ToString(), form["client_secret"].ToString(), form["scope"].ToString()), ct),
                    _ => Result<TokenResponse>.Failure(Error.Validation("unsupported_grant_type", "grant_type no soportado."))
                };

                if (result.IsFailure)
                {
                    var code = result.Error!.Code.StartsWith("invalid_", StringComparison.Ordinal) || result.Error.Code == "unsupported_grant_type"
                        ? result.Error.Code
                        : result.Error.Type == ErrorType.Unavailable ? "temporarily_unavailable" : "invalid_grant";
                    var status = result.Error.Type == ErrorType.Unavailable ? 503 : result.Error.Type == ErrorType.Unauthorized ? 401 : 400;
                    return OAuthError(code, result.Error.Message, status);
                }

                var t = result.Value;
                return Results.Json(new
                {
                    access_token = t.AccessToken,
                    token_type = t.TokenType,
                    expires_in = t.ExpiresIn,
                    refresh_token = t.RefreshToken,
                    scope = t.Scope
                });
            })
            .WithTags("OAuth2")
            .RequireRateLimiting("auth");

        app.MapGet("/.well-known/openid-configuration", (JwtSettings settings) =>
            {
                var issuer = settings.Issuer.TrimEnd('/');
                return Results.Json(new
                {
                    issuer,
                    jwks_uri = $"{issuer}/.well-known/jwks.json",
                    token_endpoint = $"{issuer}/connect/token",
                    grant_types_supported = new[] { "password", "refresh_token", "client_credentials" },
                    response_types_supported = new[] { "token" },
                    subject_types_supported = new[] { "public" },
                    id_token_signing_alg_values_supported = new[] { "RS256" },
                    token_endpoint_auth_methods_supported = new[] { "client_secret_post" },
                    claims_supported = new[] { "sub", "email", "role", "permission", "scope", "jti" }
                });
            })
            .WithTags("OAuth2");

        app.MapGet("/.well-known/jwks.json", (RsaKeyProvider keys) => Results.Json(keys.GetJwks()))
            .WithTags("OAuth2");

        return app;
    }

    private static IResult OAuthError(string error, string description, int status = 400) =>
        Results.Json(new { error, error_description = description }, statusCode: status);
}
