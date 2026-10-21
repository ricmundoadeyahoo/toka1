using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Toka.BuildingBlocks.Contracts.Events;

namespace Toka.BuildingBlocks.Infrastructure.Logging;

public static class SerilogExtensions
{
    /// <summary>
    /// Logging estructurado en JSON (formato CLEF) a consola + Seq (log centralizado).
    /// Cada evento lleva service, environment y CorrelationId para correlacionar entre servicios.
    /// </summary>
    public static WebApplicationBuilder AddTokaSerilog(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            var seqUrl = context.Configuration["Logging:SeqUrl"];

            configuration
                .ReadFrom.Configuration(context.Configuration)
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .MinimumLevel.Override("Yarp", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service", serviceName)
                .Enrich.WithProperty("environment", context.HostingEnvironment.EnvironmentName)
                .WriteTo.Console(new RenderedCompactJsonFormatter());

            if (!string.IsNullOrWhiteSpace(seqUrl))
            {
                configuration.WriteTo.Seq(seqUrl);
            }
        });

        return builder;
    }

    /// <summary>Log de cada request HTTP (método, ruta, status, duración) sin cuerpos ni secretos.</summary>
    public static IApplicationBuilder UseTokaRequestLogging(this IApplicationBuilder app)
    {
        return app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} respondió {StatusCode} en {Elapsed:0.0} ms";
            options.GetLevel = (httpContext, _, exception) =>
                exception is not null || httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error
                : httpContext.Response.StatusCode >= 400 ? LogEventLevel.Warning
                : httpContext.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
                : LogEventLevel.Information;
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("CorrelationId", CorrelationContext.CorrelationId ?? string.Empty);
                diagnosticContext.Set("UserId", httpContext.User.FindFirst("sub")?.Value ?? "anonymous");
                diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
            };
        });
    }
}
