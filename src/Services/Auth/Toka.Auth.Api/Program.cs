using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Toka.Auth.Api.Endpoints;
using Toka.Auth.Application;
using Toka.Auth.Infrastructure;
using Toka.Auth.Infrastructure.Security;
using Toka.BuildingBlocks.Infrastructure.Hosting;
using Toka.BuildingBlocks.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddTokaPlatform("auth-service");
builder.Services.AddAuthApplication();
builder.Services.AddAuthInfrastructure(builder.Configuration);

// Auth Service es el emisor: valida sus propios tokens con la clave local, sin auto-descubrimiento HTTP.
// (Configure corre antes del PostConfigure de JwtBearer, que entonces usa una configuración estática.)
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<RsaKeyProvider, JwtSettings>((options, keyProvider, settings) =>
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = settings.Issuer };
        configuration.SigningKeys.Add(keyProvider.PublicKey);
        options.Configuration = configuration;
        options.MetadataAddress = string.Empty;
    });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Anti fuerza bruta: 10 intentos/minuto por IP en endpoints de autenticación.
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

app.UseTokaPlatform();
app.UseRateLimiter();

await app.Services.InitializeAuthDatabaseAsync(app.Configuration);

app.MapAuthEndpoints();
app.MapOAuthEndpoints();

app.Run();
