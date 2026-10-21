using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog.Context;
using Toka.BuildingBlocks.Contracts.Events;

namespace Toka.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Consumidor genérico: declara una cola durable con Dead Letter Queue, la enlaza a las routing keys
/// que declara el handler y despacha cada mensaje en un scope de DI propio.
/// - Éxito: ACK.
/// - JSON inválido o error del handler: NACK sin requeue → el mensaje va a {cola}.dlq (no se pierde ni bloquea la cola).
/// Los handlers deben ser idempotentes (entrega at-least-once).
/// </summary>
public sealed class RabbitMqConsumerService<THandler>(
    IServiceScopeFactory scopeFactory,
    RabbitMqConnectionProvider connectionProvider,
    ILogger<RabbitMqConsumerService<THandler>> logger,
    string queueName) : BackgroundService
    where THandler : IIntegrationEventHandler
{
    private IModel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IReadOnlyCollection<string> bindings;
        using (var scope = scopeFactory.CreateScope())
        {
            bindings = scope.ServiceProvider.GetRequiredService<THandler>().EventTypes;
        }

        var connection = await connectionProvider.GetConnectionAsync(stoppingToken);
        var options = connectionProvider.Options;
        _channel = connection.CreateModel();
        _channel.BasicQos(0, options.PrefetchCount, global: false);

        var dlq = $"{queueName}.dlq";
        _channel.QueueDeclare(dlq, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(dlq, options.DeadLetterExchange, routingKey: queueName);

        _channel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = options.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = queueName
        });
        foreach (var routingKey in bindings)
        {
            _channel.QueueBind(queueName, options.Exchange, routingKey);
        }

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (_, delivery) => await OnMessageAsync(delivery, stoppingToken);
        _channel.BasicConsume(queueName, autoAck: false, consumer);

        logger.LogInformation("Consumidor {Queue} escuchando {Bindings}", queueName, string.Join(", ", bindings));

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // apagado ordenado
        }
    }

    private async Task OnMessageAsync(BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        IntegrationEvent? evt = null;
        try
        {
            evt = JsonSerializer.Deserialize<IntegrationEvent>(Encoding.UTF8.GetString(delivery.Body.Span), RabbitMqEventPublisher.JsonOptions);
            if (evt is null) throw new JsonException("Mensaje vacío");

            CorrelationContext.CorrelationId = evt.CorrelationId;
            using (LogContext.PushProperty("CorrelationId", evt.CorrelationId ?? string.Empty))
            using (var scope = scopeFactory.CreateScope())
            {
                var handler = scope.ServiceProvider.GetRequiredService<THandler>();
                await handler.HandleAsync(evt, stoppingToken);
            }

            _channel!.BasicAck(delivery.DeliveryTag, multiple: false);
            logger.LogInformation("Evento procesado {EventType} {EventId} en {Queue}", evt.EventType, evt.Id, queueName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error procesando mensaje {RoutingKey} {EventId} en {Queue}; enviado a DLQ",
                delivery.RoutingKey, evt?.Id, queueName);
            _channel!.BasicNack(delivery.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }
}
