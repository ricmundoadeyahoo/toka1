using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Toka.BuildingBlocks.Infrastructure.Caching;
using Toka.BuildingBlocks.Infrastructure.Http;
using Toka.BuildingBlocks.Infrastructure.Logging;
using Toka.BuildingBlocks.Infrastructure.Messaging;
using Toka.BuildingBlocks.Infrastructure.Security;

namespace Toka.BuildingBlocks.Infrastructure.Hosting;

/// <summary>
/// "Chasis" común de microservicio: todos los servicios arrancan igual (logging JSON, correlation id,
/// ProblemDetails, JWT distribuido, Redis, RabbitMQ, health checks, Swagger).
/// </summary>
public static class PlatformExtensions
{
    public static WebApplicationBuilder AddTokaPlatform(this WebApplicationBuilder builder, string serviceName, bool useJwt = true)
    {
        builder.AddTokaSerilog(serviceName);

        var services = builder.Services;
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdDelegatingHandler>();
        services.AddSingleton(TimeProvider.System);
        services.AddHealthChecks();

        services.AddTokaRedisCache(builder.Configuration);
        services.AddTokaRabbitMq(builder.Configuration);
        if (useJwt) services.AddTokaJwtAuthentication(builder.Configuration);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 2; // nginx → gateway → servicio
            options.KnownNetworks.Clear(); // red interna de Docker
            options.KnownProxies.Clear();
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = serviceName, Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Token JWT emitido por Auth Service"
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                    Array.Empty<string>()
                }
            });
        });

        return builder;
    }

    public static WebApplication UseTokaPlatform(this WebApplication app, bool useJwt = true)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseTokaRequestLogging();

        app.Use(async (context, next) =>
        {
            // Cabeceras de seguridad básicas (OWASP Secure Headers)
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            await next();
        });

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        if (useJwt)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        // Liveness: el proceso responde. Readiness: dependencias críticas (checks registrados por cada servicio).
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready");

        return app;
    }

    /// <summary>
    /// Política de resiliencia estándar para HttpClient entre servicios:
    /// rate limiter → timeout total → retry exponencial con jitter → circuit breaker → timeout por intento.
    /// </summary>
    public static IHttpClientBuilder AddTokaResilience(this IHttpClientBuilder builder, TimeSpan? attemptTimeout = null, TimeSpan? totalTimeout = null, int retries = 3)
    {
        builder.AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
        builder.AddStandardResilienceHandler(options =>
        {
            var attempt = attemptTimeout ?? TimeSpan.FromSeconds(5);
            options.AttemptTimeout.Timeout = attempt;
            options.TotalRequestTimeout.Timeout = totalTimeout ?? TimeSpan.FromSeconds(20);
            options.Retry.MaxRetryAttempts = retries;
            options.Retry.UseJitter = true;
            // El SamplingDuration del circuit breaker debe ser >= 2x el timeout por intento.
            options.CircuitBreaker.SamplingDuration = attempt * 2 > TimeSpan.FromSeconds(30) ? attempt * 2 : TimeSpan.FromSeconds(30);
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.MinimumThroughput = 5;
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
        });
        return builder;
    }
}
