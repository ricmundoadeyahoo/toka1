using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Toka.BuildingBlocks.Contracts.Events;

namespace Toka.BuildingBlocks.Infrastructure.Messaging;

public static class MessagingExtensions
{
    public static IServiceCollection AddTokaRabbitMq(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        return services;
    }

    /// <summary>Registra un handler y su consumidor en segundo plano sobre la cola indicada.</summary>
    public static IServiceCollection AddEventConsumer<THandler>(this IServiceCollection services, string queueName)
        where THandler : class, IIntegrationEventHandler
    {
        services.AddScoped<THandler>();
        services.AddHostedService(sp => new RabbitMqConsumerService<THandler>(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<RabbitMqConnectionProvider>(),
            sp.GetRequiredService<ILogger<RabbitMqConsumerService<THandler>>>(),
            queueName));
        return services;
    }
}
