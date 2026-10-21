using Microsoft.Extensions.DependencyInjection;
using Toka.Auth.Application.EventHandlers;
using Toka.Auth.Application.UseCases;

namespace Toka.Auth.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthApplication(this IServiceCollection services)
    {
        services.AddScoped<TokenIssuer>();
        services.AddScoped<RegisterUseCase>();
        services.AddScoped<LoginUseCase>();
        services.AddScoped<RefreshTokenUseCase>();
        services.AddScoped<LogoutUseCase>();
        services.AddScoped<ClientCredentialsUseCase>();
        services.AddScoped<UserStatusChangedHandler>();
        return services;
    }
}
