using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Toka.BuildingBlocks.Contracts.Events;

namespace Toka.User.Application;

/// <summary>Consume identity.user.registered (asíncrono, RabbitMQ) y crea el perfil.</summary>
public sealed class UserRegisteredHandler(UserCommands commands, ILogger<UserRegisteredHandler> logger) : IIntegrationEventHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } = [global::Toka.BuildingBlocks.Contracts.Events.EventTypes.UserRegistered];

    public async Task HandleAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(integrationEvent.SubjectId, out var userId)
            || !integrationEvent.Data.TryGetValue("email", out var email)
            || !integrationEvent.Data.TryGetValue("fullName", out var fullName))
        {
            // Mensaje mal formado: se lanza para que vaya a la DLQ y se pueda inspeccionar.
            throw new InvalidOperationException($"Evento {integrationEvent.Id} incompleto para crear perfil.");
        }

        integrationEvent.Data.TryGetValue("department", out var department);
        var created = await commands.CreateFromRegistrationAsync(userId, email, fullName, department, cancellationToken);
        logger.LogInformation(created ? "Perfil creado para {UserId}" : "Perfil {UserId} ya existía (evento duplicado)", userId);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddUserApplication(this IServiceCollection services)
    {
        services.AddScoped<UserQueries>();
        services.AddScoped<UserCommands>();
        return services;
    }
}
