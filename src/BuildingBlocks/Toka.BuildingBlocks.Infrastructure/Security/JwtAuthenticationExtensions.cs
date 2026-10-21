using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Toka.BuildingBlocks.Contracts.Caching;
using Toka.BuildingBlocks.Contracts.Security;

namespace Toka.BuildingBlocks.Infrastructure.Security;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    /// <summary>Emisor esperado (iss). Debe coincidir con el issuer del documento OIDC de Auth Service.</summary>
    public string Issuer { get; set; } = "http://auth-service:8080";

    /// <summary>Audiencia esperada (aud).</summary>
    public string Audience { get; set; } = "toka-api";

    /// <summary>URL base desde la que se descubre la configuración OIDC (/.well-known/openid-configuration).</summary>
    public string Authority { get; set; } = "http://auth-service:8080";

    /// <summary>Solo para entorno local dockerizado sin TLS. En producción: true.</summary>
    public bool RequireHttpsMetadata { get; set; }

    public bool CheckRevocation { get; set; } = true;
}

public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// Validación DISTRIBUIDA de JWT (RS256): cada microservicio descarga la clave pública desde el
    /// endpoint JWKS de Auth Service (vía discovery OIDC) y valida localmente firma, iss, aud y exp.
    /// No hay llamada a Auth Service por request; las claves se cachean y se refrescan automáticamente.
    /// Adicionalmente se consulta en Redis la lista de tokens revocados (logout).
    /// </summary>
    public static IServiceCollection AddTokaJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<JwtBearerOptions>? configure = null)
    {
        var settings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
        services.AddSingleton(settings);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress = $"{settings.Authority.TrimEnd('/')}/.well-known/openid-configuration";
                options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
                options.MapInboundClaims = false; // conserva "sub", "role", "permission" tal cual
                options.RefreshOnIssuerKeyNotFound = true; // rotación de claves
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    NameClaimType = TokaClaimTypes.Email,
                    RoleClaimType = TokaClaimTypes.Role,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        if (!settings.CheckRevocation) return;
                        var jti = context.Principal?.FindFirst(TokaClaimTypes.JwtId)?.Value;
                        if (string.IsNullOrEmpty(jti)) return;

                        var cache = context.HttpContext.RequestServices.GetService<IDistributedCache>();
                        if (cache is null) return;
                        try
                        {
                            var revoked = await cache.GetStringAsync(CacheKeys.RevokedToken(jti), context.HttpContext.RequestAborted);
                            if (revoked is not null) context.Fail("Token revocado");
                        }
                        catch (Exception ex)
                        {
                            // Fail-open consciente: si Redis cae, se prioriza disponibilidad (el token expira en minutos).
                            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                                .CreateLogger("JwtRevocation");
                            logger.LogWarning(ex, "No se pudo verificar revocación del token {Jti}", jti);
                        }
                    }
                };
                configure?.Invoke(options);
            });

        services.AddTokaAuthorization();
        return services;
    }

    /// <summary>Una política por permiso: endpoints declaran RequireAuthorization(Permissions.X).</summary>
    public static IServiceCollection AddTokaAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(permission, policy =>
                    policy.RequireAuthenticatedUser().RequireClaim(TokaClaimTypes.Permission, permission));
            }
        });
        return services;
    }
}
