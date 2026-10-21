using Microsoft.Extensions.Logging;
using Toka.BuildingBlocks.Contracts.Events;

namespace Toka.Auth.Application.EventHandlers;

/// <summary>
/// Mantiene la consistencia eventual: cuando User Service desactiva/activa un usuario,
/// Auth bloquea/desbloquea su credencial. Idempotente: aplicar dos veces no cambia el resultado.
/// </summary>
public sealed class UserStatusChangedHandler(ICredentialRepository repository, ILogger<UserStatusChangedHandler> logger) : IIntegrationEventHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } =
        [Toka.BuildingBlocks.Contracts.Events.EventTypes.UserDeactivated, Toka.BuildingBlocks.Contracts.Events.EventTypes.UserActivated];

    public async Task HandleAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(integrationEvent.SubjectId, out var userId))
        {
            logger.LogWarning("Evento {EventId} sin SubjectId válido; se ignora", integrationEvent.Id);
            return;
        }

        var credential = await repository.GetByIdAsync(userId, cancellationToken);
        if (credential is null)
        {
            logger.LogWarning("Credencial {UserId} no encontrada para {EventType}", userId, integrationEvent.EventType);
            return;
        }

        if (integrationEvent.EventType == Toka.BuildingBlocks.Contracts.Events.EventTypes.UserDeactivated)
            credential.Deactivate();
        else
            credential.Activate();

        await repository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Credencial {UserId} actualizada por {EventType}", userId, integrationEvent.EventType);
    }
}
