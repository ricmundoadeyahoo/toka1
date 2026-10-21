using Microsoft.Extensions.Options;
using Toka.Auth.Domain;
using Toka.BuildingBlocks.Contracts.Results;

namespace Toka.Auth.Application.UseCases;

/// <summary>Emite el par access/refresh token para un usuario, con roles y permisos vigentes.</summary>
public sealed class TokenIssuer(
    ITokenService tokenService,
    IRefreshTokenStore refreshTokenStore,
    IRoleServiceClient roleServiceClient,
    IOptions<AuthOptions> options,
    TimeProvider timeProvider)
{
    public async Task<Result<TokenResponse>> IssueAsync(UserCredential credential, CancellationToken ct)
    {
        var authorization = await roleServiceClient.GetUserAuthorizationAsync(credential.Id, ct);
        if (authorization is null)
            return Error.Unavailable("auth.roles_unavailable", "No fue posible obtener los roles del usuario. Intenta de nuevo en unos segundos.");

        var access = tokenService.CreateAccessToken(new AccessTokenRequest(
            credential.Id.ToString(), credential.Email, null, authorization.Roles, authorization.Permissions, []));

        var refreshToken = tokenService.CreateRefreshToken();
        var refreshExpires = timeProvider.GetUtcNow().AddDays(options.Value.RefreshTokenDays);
        await refreshTokenStore.StoreAsync(refreshToken, new RefreshTokenEntry(credential.Id, refreshExpires), ct);

        var expiresIn = (int)Math.Max(0, (access.ExpiresAt - timeProvider.GetUtcNow()).TotalSeconds);
        return Result<TokenResponse>.Success(new TokenResponse(
            access.Token,
            "Bearer",
            expiresIn,
            refreshToken,
            null,
            new AuthenticatedUser(credential.Id, credential.Email, authorization.Roles, authorization.Permissions)));
    }
}
