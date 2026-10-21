using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Toka.BuildingBlocks.Contracts.Events;

namespace Toka.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// Traduce excepciones técnicas a ProblemDetails (RFC 7807) sin filtrar stack traces (OWASP A05).
/// Los errores de negocio esperados NO llegan aquí: viajan como Result.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Solicitud inválida"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Solicitud inválida"),
            BrokenCircuitException => (StatusCodes.Status503ServiceUnavailable, "Dependencia no disponible (circuit breaker abierto)"),
            TimeoutRejectedException => (StatusCodes.Status504GatewayTimeout, "Tiempo de espera agotado con una dependencia"),
            HttpRequestException => (StatusCodes.Status502BadGateway, "Error al comunicarse con otro servicio"),
            OperationCanceledException => (499, "Solicitud cancelada"),
            _ => (StatusCodes.Status500InternalServerError, "Error interno del servidor")
        };

        if (status >= 500)
            logger.LogError(exception, "Excepción no controlada: {ExceptionType}", exception.GetType().Name);
        else
            logger.LogWarning("Solicitud rechazada por {ExceptionType}: {Message}", exception.GetType().Name, exception.Message);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Instance = httpContext.Request.Path,
            Detail = status == 400 ? exception.Message : null
        };
        problem.Extensions["correlationId"] = CorrelationContext.CorrelationId;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
