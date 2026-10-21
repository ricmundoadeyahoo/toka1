using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Toka.Auth.Domain;
using Toka.BuildingBlocks.Contracts.Events;
using Toka.BuildingBlocks.Contracts.Results;

namespace Toka.Auth.Application.UseCases;

/// <summary>
/// Login con protección OWASP: mensaje genérico (sin enumeración de usuarios), tiempo de respuesta
/// homogéneo, bloqueo temporal tras N intentos y auditoría de cada intento vía eventos.
/// </summary>
public sealed class LoginUseCase(
    ICredentialRepository repository,
    IPasswordHasher passwordHasher,
    TokenIssuer tokenIssuer,
    IEventPublisher eventPublisher,
    IOptions<AuthOptions> options,
    TimeProvider timeProvider,
    ILogger<LoginUseCase> logger)
{
    internal static readonly Error InvalidCredentials = Error.Unauthorized("auth.invalid_credentials", "Email o contraseña incorrectos.");

    public async Task<Result<TokenResponse>> ExecuteAsync(LoginCommand command, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        if (!EmailAddress.TryCreate(command.Email, out var email) || string.IsNullOrEmpty(command.Password))
            return InvalidCredentials;

        var credential = await repository.GetByEmailAsync(email!.Value, ct);
        if (credential is null)
        {
            passwordHasher.Hash(command.Password); // iguala el tiempo de respuesta (anti timing attack)
            await PublishFailureAsync(null, email.Value, command.IpAddress, "unknown_user", ct);
            return InvalidCredentials;
        }

        if (credential.IsLockedOut(now))
        {
            await PublishFailureAsync(credential.Id, credential.Email, command.IpAddress, "locked_out", ct);
            return Error.Forbidden("auth.locked_out", "La cuenta está bloqueada temporalmente por intentos fallidos.");
        }

        if (!credential.IsActive)
        {
            await PublishFailureAsync(credential.Id, credential.Email, command.IpAddress, "inactive", ct);
            return Error.Forbidden("auth.inactive", "La cuenta está desactivada.");
        }

        if (!passwordHasher.Verify(command.Password, credential.PasswordHash))
        {
            var locked = credential.RegisterFailedLogin(now, options.Value.MaxFailedAttempts, TimeSpan.FromMinutes(options.Value.LockoutMinutes));
            await repository.SaveChangesAsync(ct);
            await PublishFailureAsync(credential.Id, credential.Email, command.IpAddress, "bad_password", ct);
            if (locked)
            {
                logger.LogWarning("Cuenta bloqueada por intentos fallidos {UserId}", credential.Id);
                await eventPublisher.PublishAsync(new IntegrationEvent
                {
                    EventType = EventTypes.AccountLocked,
                    Source = "auth-service",
                    SubjectId = credential.Id.ToString(),
                    ActorEmail = credential.Email,
                    IpAddress = command.IpAddress,
                    Success = false,
                    Data = new() { ["lockoutMinutes"] = options.Value.LockoutMinutes.ToString() }
                }, ct);
            }
            return InvalidCredentials;
        }

        var tokens = await tokenIssuer.IssueAsync(credential, ct);
        if (tokens.IsFailure) return tokens;

        credential.RegisterSuccessfulLogin(now);
        await repository.SaveChangesAsync(ct);

        await eventPublisher.PublishAsync(new IntegrationEvent
        {
            EventType = EventTypes.LoginSucceeded,
            Source = "auth-service",
            ActorId = credential.Id.ToString(),
            ActorEmail = credential.Email,
            SubjectId = credential.Id.ToString(),
            IpAddress = command.IpAddress,
            Data = new() { ["roles"] = string.Join(",", tokens.Value.User!.Roles) }
        }, ct);

        return tokens;
    }

    private Task PublishFailureAsync(Guid? userId, string email, string? ip, string reason, CancellationToken ct) =>
        eventPublisher.PublishAsync(new IntegrationEvent
        {
            EventType = EventTypes.LoginFailed,
            Source = "auth-service",
            SubjectId = userId?.ToString(),
            ActorEmail = email,
            IpAddress = ip,
            Success = false,
            Data = new() { ["reason"] = reason }
        }, ct);
}
