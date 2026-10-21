using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Toka.BuildingBlocks.Contracts.Events;

namespace Toka.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Publica eventos persistentes en el exchange topic con publisher confirms.
/// Un fallo al publicar se registra y NO revierte la operación de negocio ya confirmada
/// (ver Sugerencias: patrón Transactional Outbox para garantía at-least-once estricta).
/// </summary>
public sealed class RabbitMqEventPublisher(RabbitMqConnectionProvider connectionProvider, ILogger<RabbitMqEventPublisher> logger)
    : IEventPublisher, IDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private IModel? _channel;

    public async Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var evt = integrationEvent.CorrelationId is null
            ? integrationEvent with { CorrelationId = CorrelationContext.CorrelationId }
            : integrationEvent;

        try
        {
            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
            await _channelLock.WaitAsync(cancellationToken); // IModel no es thread-safe
            try
            {
                if (_channel is null || _channel.IsClosed)
                {
                    _channel = connection.CreateModel();
                    _channel.ConfirmSelect();
                }

                var props = _channel.CreateBasicProperties();
                props.Persistent = true;
                props.ContentType = "application/json";
                props.MessageId = evt.Id.ToString();
                props.CorrelationId = evt.CorrelationId;
                props.Type = evt.EventType;
                props.Timestamp = new AmqpTimestamp(evt.OccurredAt.ToUnixTimeSeconds());

                var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evt, JsonOptions));
                _channel.BasicPublish(connectionProvider.Options.Exchange, evt.EventType, mandatory: false, props, body);
                _channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
            }
            finally
            {
                _channelLock.Release();
            }

            logger.LogInformation("Evento publicado {EventType} {EventId}", evt.EventType, evt.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "No se pudo publicar el evento {EventType} {EventId}", evt.EventType, evt.Id);
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _channelLock.Dispose();
    }
}
