using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Serilog.Context;
using Toka.BuildingBlocks.Contracts.Events;

namespace Toka.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// Lee X-Correlation-ID (o genera uno), lo expone en la respuesta y lo agrega al LogContext.
/// Se valida el formato para evitar log injection con valores arbitrarios.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsValid(incoming) ? incoming : Guid.NewGuid().ToString("N");

        CorrelationContext.CorrelationId = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && SafeId().IsMatch(value);

    [GeneratedRegex("^[a-zA-Z0-9\\-_.]+$")]
    private static partial Regex SafeId();
}

/// <summary>Propaga el Correlation ID en llamadas HTTP salientes entre microservicios.</summary>
public sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var id = CorrelationContext.CorrelationId;
        if (!string.IsNullOrEmpty(id) && !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
        {
            request.Headers.Add(CorrelationIdMiddleware.HeaderName, id);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
