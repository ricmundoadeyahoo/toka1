namespace Toka.BuildingBlocks.Contracts.Events;

/// <summary>Puerto de salida para publicar eventos. Implementado con RabbitMQ en Infrastructure.</summary>
public interface IEventPublisher
{
    Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}

/// <summary>Manejador de eventos entrantes. Declara qué routing keys le interesan.</summary>
public interface IIntegrationEventHandler
{
    /// <summary>Patrones de routing key (admite comodines de topic: * y #).</summary>
    IReadOnlyCollection<string> EventTypes { get; }

    Task HandleAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Correlation ID ambiental (AsyncLocal). El middleware HTTP y el consumidor de RabbitMQ lo
/// fijan; el publicador y los HttpClients lo propagan. Permite seguir una petición entre servicios.
/// </summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? CorrelationId
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}
