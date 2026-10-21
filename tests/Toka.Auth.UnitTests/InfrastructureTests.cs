using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Toka.Auth.Application;
using Toka.Auth.Infrastructure.Security;
using Toka.BuildingBlocks.Infrastructure.Security;
using Toka.Tests.Shared;

namespace Toka.Auth.UnitTests;

public class InfrastructureTests : IDisposable
{
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"toka-test-{Guid.NewGuid():N}.pem");

    private RsaKeyProvider NewKeyProvider() =>
        new(Options.Create(new SigningKeyOptions { Path = _keyPath }), NullLogger<RsaKeyProvider>.Instance);

    [Fact]
    public async Task Jwt_firmado_es_validable_con_la_clave_publica_del_JWKS()
    {
        using var keys = NewKeyProvider();
        var settings = new JwtSettings { Issuer = "http://auth-service:8080", Audience = "toka-api" };
        var clock = FixedTimeProvider.Default();
        var service = new JwtTokenService(keys, settings, Options.Create(new AuthOptions { AccessTokenMinutes = 15 }), TimeProvider.System);

        var issued = service.CreateAccessToken(new AccessTokenRequest(
            Guid.NewGuid().ToString(), "a@toka.com", null, ["Admin"], ["users:read", "users:write"], []));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.Token, new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Audience,
            IssuerSigningKey = keys.PublicKey
        });

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal("a@toka.com", result.Claims["email"]);
        Assert.Equal(issued.JwtId, result.Claims["jti"]);
        Assert.NotNull(clock);
    }

    [Fact]
    public void Clave_se_persiste_y_se_reutiliza()
    {
        string kid1, kid2;
        using (var first = NewKeyProvider()) kid1 = first.KeyId;
        Assert.True(File.Exists(_keyPath));
        using (var second = NewKeyProvider()) kid2 = second.KeyId;
        Assert.Equal(kid1, kid2);
    }

    [Fact]
    public void Jwks_expone_solo_la_parte_publica()
    {
        using var keys = NewKeyProvider();
        var json = JsonSerializer.Serialize(keys.GetJwks());
        using var doc = JsonDocument.Parse(json);
        var key = doc.RootElement.GetProperty("keys")[0];
        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.Equal(keys.KeyId, key.GetProperty("kid").GetString());
        Assert.False(key.TryGetProperty("d", out _)); // exponente privado jamás se publica
    }

    [Fact]
    public void Refresh_token_es_aleatorio_y_largo()
    {
        using var keys = NewKeyProvider();
        var service = new JwtTokenService(keys, new JwtSettings(), Options.Create(new AuthOptions()), TimeProvider.System);
        var a = service.CreateRefreshToken();
        var b = service.CreateRefreshToken();
        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 40);
    }

    [Fact]
    public void BCrypt_hashea_y_verifica()
    {
        var hasher = new BCryptPasswordHasher();
        var hash = hasher.Hash("Segura#2026");
        Assert.StartsWith("$2", hash);
        Assert.True(hasher.Verify("Segura#2026", hash));
        Assert.False(hasher.Verify("Otra#2026", hash));
        Assert.False(hasher.Verify("x", "no-es-un-hash"));
    }

    [Fact]
    public void Hash_de_refresh_token_es_determinista_y_no_reversible()
    {
        var h1 = RedisRefreshTokenStore.Hash("token");
        Assert.Equal(h1, RedisRefreshTokenStore.Hash("token"));
        Assert.DoesNotContain("token", h1, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(64, h1.Length);
    }

    public void Dispose()
    {
        if (File.Exists(_keyPath)) File.Delete(_keyPath);
        GC.SuppressFinalize(this);
    }
}
