using System.Security.Cryptography;
using System.Text;
using Toka.BuildingBlocks.Contracts.Events;
using Toka.BuildingBlocks.Contracts.Results;

namespace Toka.Auth.Application.UseCases;

/// <summary>Refresh token con rotación: cada refresh token es de un solo uso.</summary>
public sealed class RefreshTokenUseCase(
    IRefreshTokenStore refreshTokenStore,
    ICredentialRepository repository,
    TokenIssuer tokenIssuer,
    TimeProvider timeProvider)
{
    public async Task<Result<TokenResponse>> ExecuteAsync(RefreshCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken) || command.RefreshToken.Length > 200)
            return Error.Unauthorized("auth.refresh_invalid", "Refresh token inválido.");

        var entry = await refreshTokenStore.ConsumeAsync(command.RefreshToken, ct);
        if (entry is null || entry.ExpiresAt <= timeProvider.GetUtcNow())
            return Error.Unauthorized("auth.refresh_invalid", "Refresh token inválido o expirado.");

        var credential = await repository.GetByIdAsync(entry.UserId, ct);
        if (credential is null || !credential.IsActive)
            return Error.Forbidden("auth.inactive", "La cuenta no está activa.");

        return await tokenIssuer.IssueAsync(credential, ct);
    }
}

/// <summary>Logout: revoca el access token (jti en Redis hasta su expiración) y el refresh token.</summary>
public sealed class LogoutUseCase(
    ITokenRevocationStore revocationStore,
    IRefreshTokenStore refreshTokenStore,
    IEventPublisher eventPublisher)
{
    public async Task<Result> ExecuteAsync(LogoutCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.JwtId))
            return Error.Validation("auth.jti_missing", "El token no contiene identificador (jti).");

        await revocationStore.RevokeAccessTokenAsync(command.JwtId, command.AccessTokenExpiresAt, ct);
        if (!string.IsNullOrWhiteSpace(command.RefreshToken))
            await refreshTokenStore.RevokeAsync(command.RefreshToken, ct);

        await eventPublisher.PublishAsync(new IntegrationEvent
        {
            EventType = EventTypes.LoggedOut,
            Source = "auth-service",
            ActorId = command.UserId.ToString(),
            ActorEmail = command.Email,
            SubjectId = command.UserId.ToString()
        }, ct);

        return Result.Success();
    }
}

/// <summary>
/// OAuth2 client_credentials para comunicación servicio-a-servicio (p. ej. AI Service → User Service
/// sin contexto de usuario). Comparación de secretos en tiempo constante.
/// </summary>
public sealed class ClientCredentialsUseCase(IClientRegistry clientRegistry, ITokenService tokenService, TimeProvider timeProvider)
{
    public Task<Result<TokenResponse>> ExecuteAsync(ClientCredentialsCommand command, CancellationToken ct)
    {
        var client = string.IsNullOrWhiteSpace(command.ClientId) ? null : clientRegistry.Find(command.ClientId);
        if (client is null || !SecretsEqual(client.ClientSecret, command.ClientSecret))
            return Task.FromResult(Result<TokenResponse>.Failure(Error.Unauthorized("invalid_client", "Cliente o secreto inválido.")));

        var requested = string.IsNullOrWhiteSpace(command.Scope)
            ? client.AllowedScopes.ToList()
            : command.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();

        if (requested.Any(s => !client.AllowedScopes.Contains(s)))
            return Task.FromResult(Result<TokenResponse>.Failure(Error.Forbidden("invalid_scope", "El cliente no tiene permitido alguno de los scopes solicitados.")));

        var token = tokenService.CreateAccessToken(new AccessTokenRequest(client.ClientId, null, null, [], [], requested, client.ClientId));
        var expiresIn = (int)Math.Max(0, (token.ExpiresAt - timeProvider.GetUtcNow()).TotalSeconds);
        return Task.FromResult(Result<TokenResponse>.Success(
            new TokenResponse(token.Token, "Bearer", expiresIn, null, string.Join(' ', requested), null)));
    }

    internal static bool SecretsEqual(string expected, string? provided)
    {
        if (provided is null) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
