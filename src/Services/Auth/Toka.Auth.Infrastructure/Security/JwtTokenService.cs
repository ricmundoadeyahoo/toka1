using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Toka.Auth.Application;
using Toka.BuildingBlocks.Contracts.Security;
using Toka.BuildingBlocks.Infrastructure.Security;

namespace Toka.Auth.Infrastructure.Security;

/// <summary>Emite JWT firmados con RS256. Los demás servicios validan con la clave pública (JWKS).</summary>
public sealed class JwtTokenService(
    RsaKeyProvider keyProvider,
    JwtSettings jwtSettings,
    IOptions<AuthOptions> authOptions,
    TimeProvider timeProvider) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public IssuedAccessToken CreateAccessToken(AccessTokenRequest request)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(authOptions.Value.AccessTokenMinutes);
        var jti = Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new(TokaClaimTypes.Subject, request.Subject),
            new(TokaClaimTypes.JwtId, jti)
        };
        if (!string.IsNullOrEmpty(request.Email)) claims.Add(new Claim(TokaClaimTypes.Email, request.Email));
        if (!string.IsNullOrEmpty(request.Name)) claims.Add(new Claim(TokaClaimTypes.Name, request.Name));
        if (!string.IsNullOrEmpty(request.ClientId)) claims.Add(new Claim(TokaClaimTypes.ClientId, request.ClientId));
        claims.AddRange(request.Roles.Select(r => new Claim(TokaClaimTypes.Role, r)));
        claims.AddRange(request.Permissions.Select(p => new Claim(TokaClaimTypes.Permission, p)));
        if (request.Scopes.Count > 0) claims.Add(new Claim(TokaClaimTypes.Scope, string.Join(' ', request.Scopes)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwtSettings.Issuer,
            Audience = jwtSettings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(keyProvider.SigningKey, SecurityAlgorithms.RsaSha256)
        };

        return new IssuedAccessToken(_handler.CreateToken(descriptor), jti, expires);
    }

    /// <summary>Refresh token opaco de 256 bits (no es JWT: no transporta claims).</summary>
    public string CreateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
}
