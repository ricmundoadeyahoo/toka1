using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Toka.BuildingBlocks.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string Exchange { get; set; } = "toka.events";
    public string DeadLetterExchange { get; set; } = "toka.events.dlx";
    public ushort PrefetchCount { get; set; } = 10;
}

/// <summary>
/// Conexión única por proceso (recomendación de RabbitMQ). Reintenta con backoff exponencial
/// al arrancar, porque en Docker Compose el broker puede tardar en estar disponible.
/// Recuperación automática habilitada para reconexiones posteriores.
/// </summary>
public sealed class RabbitMqConnectionProvider(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnectionProvider> logger) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqOptions Options => options.Value;

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true }) return _connection;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;

            var factory = new ConnectionFactory
            {
                HostName = Options.Host,
                Port = Options.Port,
                UserName = Options.UserName,
                Password = Options.Password,
                VirtualHost = Options.VirtualHost,
                DispatchConsumersAsync = true,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                ClientProvidedName = AppDomain.CurrentDomain.FriendlyName
            };

            var delay = TimeSpan.FromSeconds(2);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    _connection = factory.CreateConnection();
                    DeclareTopology(_connection);
                    logger.LogInformation("Conectado a RabbitMQ {Host}:{Port} (intento {Attempt})", Options.Host, Options.Port, attempt);
                    return _connection;
                }
                catch (Exception ex) when (attempt < 10 && !cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "RabbitMQ no disponible (intento {Attempt}); reintentando en {Delay}s", attempt, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private void DeclareTopology(IConnection connection)
    {
        using var channel = connection.CreateModel();
        channel.ExchangeDeclare(Options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        channel.ExchangeDeclare(Options.DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false);
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _lock.Dispose();
    }
}
