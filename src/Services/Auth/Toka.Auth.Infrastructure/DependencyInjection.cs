using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Toka.Auth.Application;
using Toka.Auth.Application.EventHandlers;
using Toka.Auth.Domain;
using Toka.Auth.Infrastructure.Clients;
using Toka.Auth.Infrastructure.Persistence;
using Toka.Auth.Infrastructure.Security;
using Toka.BuildingBlocks.Contracts.Security;
using Toka.BuildingBlocks.Infrastructure.Hosting;
using Toka.BuildingBlocks.Infrastructure.Messaging;

namespace Toka.Auth.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<SigningKeyOptions>(configuration.GetSection(SigningKeyOptions.SectionName));

        services.AddDbContext<AuthDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("AuthDb"),
                npgsql => npgsql.EnableRetryOnFailure(5)));
        services.AddHealthChecks().AddDbContextCheck<AuthDbContext>("postgres");

        services.AddScoped<ICredentialRepository, CredentialRepository>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<RsaKeyProvider>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IRefreshTokenStore, RedisRefreshTokenStore>();
        services.AddScoped<ITokenRevocationStore, RedisTokenRevocationStore>();
        services.AddSingleton<IClientRegistry, ConfigurationClientRegistry>();

        services.AddHttpClient<IRoleServiceClient, RoleServiceClient>(client =>
                client.BaseAddress = new Uri(configuration["Services:RoleService"] ?? "http://role-service:8080"))
            .AddTokaResilience();

        services.AddEventConsumer<UserStatusChangedHandler>("auth-service.user-status");
        return services;
    }

    /// <summary>Crea el esquema (EnsureCreated) y la credencial del administrador semilla.</summary>
    public static async Task InitializeAuthDatabaseAsync(this IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AuthDbInit");
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        await StartupRetry.RunAsync(async () =>
        {
            await db.Database.EnsureCreatedAsync();
            if (!await db.Credentials.AnyAsync(c => c.Id == SeedData.AdminUserId))
            {
                var password = configuration["Seed:AdminPassword"] ?? "Admin123!";
                db.Credentials.Add(UserCredential.Register(
                    SeedData.AdminUserId, EmailAddress.Create(SeedData.AdminEmail), hasher.Hash(password), DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
                logger.LogInformation("Administrador semilla creado ({Email})", SeedData.AdminEmail);
            }
        }, logger, "Inicialización de base de datos Auth");
    }
}
