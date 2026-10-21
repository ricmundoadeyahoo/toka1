using Microsoft.Extensions.Logging;

namespace Toka.BuildingBlocks.Infrastructure.Hosting;

/// <summary>Reintenta tareas de arranque (crear BD, índices, seed) mientras las dependencias levantan.</summary>
public static class StartupRetry
{
    public static async Task RunAsync(Func<Task> action, ILogger logger, string description, int maxAttempts = 15, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                logger.LogInformation("{Description} completado", description);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("{Description} falló (intento {Attempt}/{Max}): {Error}", description, attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(2 * attempt, 10)), cancellationToken);
            }
        }
    }
}
