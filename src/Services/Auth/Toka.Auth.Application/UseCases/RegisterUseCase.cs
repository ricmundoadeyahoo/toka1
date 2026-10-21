using Microsoft.Extensions.Logging;
using Toka.Auth.Domain;
using Toka.BuildingBlocks.Contracts.Events;
using Toka.BuildingBlocks.Contracts.Results;
using Toka.BuildingBlocks.Contracts.Validation;

namespace Toka.Auth.Application.UseCases;

/// <summary>
/// Registro: crea la credencial (fuente de verdad de identidad) y publica identity.user.registered.
/// User Service crea el perfil y Role Service asigna el rol por defecto de forma ASÍNCRONA.
/// </summary>
public sealed class RegisterUseCase(
    ICredentialRepository repository,
    IPasswordHasher passwordHasher,
    IEventPublisher eventPublisher,
    TimeProvider timeProvider,
    ILogger<RegisterUseCase> logger)
{
    public async Task<Result<RegisterResponse>> ExecuteAsync(RegisterCommand command, CancellationToken ct)
    {
        if (!EmailAddress.TryCreate(command.Email, out var email))
            return Error.Validation("auth.email_invalid", "El email no tiene un formato válido.");

        var passwordErrors = PasswordPolicy.Validate(command.Password);
        if (passwordErrors.Count > 0)
            return Error.Validation("auth.password_weak", string.Join(" ", passwordErrors));

        var nameError = TextInput.Validate(command.FullName, "El nombre", 2, 120);
        if (nameError is not null) return Error.Validation("auth.fullname_invalid", nameError);

        var departmentError = TextInput.Validate(command.Department, "El departamento", 2, 80, required: false);
        if (departmentError is not null) return Error.Validation("auth.department_invalid", departmentError);

        if (await repository.ExistsByEmailAsync(email!.Value, ct))
            return Error.Conflict("auth.email_taken", "Ya existe una cuenta con ese email.");

        var now = timeProvider.GetUtcNow();
        var credential = UserCredential.Register(Guid.NewGuid(), email, passwordHasher.Hash(command.Password), now);
        await repository.AddAsync(credential, ct);
        await repository.SaveChangesAsync(ct);

        var data = new Dictionary<string, string>
        {
            ["email"] = credential.Email,
            ["fullName"] = TextInput.Normalize(command.FullName)!
        };
        var department = TextInput.Normalize(command.Department);
        if (department is not null) data["department"] = department;

        await eventPublisher.PublishAsync(new IntegrationEvent
        {
            EventType = EventTypes.UserRegistered,
            Source = "auth-service",
            ActorId = credential.Id.ToString(),
            ActorEmail = credential.Email,
            SubjectId = credential.Id.ToString(),
            IpAddress = command.IpAddress,
            OccurredAt = now,
            Data = data
        }, ct);

        logger.LogInformation("Usuario registrado {UserId}", credential.Id);
        return Result<RegisterResponse>.Success(new RegisterResponse(credential.Id, credential.Email));
    }
}
