using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Toka.BuildingBlocks.Infrastructure.Http;
using Toka.BuildingBlocks.Infrastructure.Logging;

// API Gateway / BFF: punto de entrada único para el front-end.
// Enruta por prefijo, aplica rate limiting global, CORS y propaga Correlation ID.
// La autorización se hace en cada microservicio (zero-trust: el gateway no es la única barrera).
var builder = WebApplication.CreateBuilder(args);

builder.AddTokaSerilog("api-gateway");

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddHealthChecks();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:3000", "http://localhost:5173" };
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // 300 req/min por IP (protección básica contra abuso / DoS de capa 7).
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseTokaRequestLogging();
app.UseCors();
app.UseRateLimiter();

app.MapHealthChecks("/health/live");
app.MapReverseProxy();

app.Run();
