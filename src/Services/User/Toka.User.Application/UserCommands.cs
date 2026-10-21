using Toka.BuildingBlocks.Contracts.Caching;
using Toka.BuildingBlocks.Contracts.Events;
using Toka.BuildingBlocks.Contracts.Results;
using Toka.BuildingBlocks.Contracts.Validation;
using Toka.User.Domain;

namespace Toka.User.Application;

/// <summary>Lado de escritura: modifica el agregado, invalida caché y publica eventos de dominio.</summary>
public sealed class UserCommands(IUserRepository repository, ICacheService cache, IEventPublisher eventPublisher, TimeProvider timeProvider)
{
    public async Task<Result<UserDto>> UpdateAsync(UpdateUserCommand command, CancellationToken ct)
    {
        if (!command.Actor.CanWriteUsers && command.Actor.UserId != command.UserId)
            return Error.Forbidden("users.forbidden", "Solo puedes editar tu propio perfil.");

        foreach (var (value, field, min, max, required) in new[]
                 {
                     (command.FullName, "El nombre", 2, UserProfile.MaxNameLength, true),
                     (command.Department, "El departamento", 2, UserProfile.MaxDepartmentLength, false),
                     (command.JobTitle, "El puesto", 2, UserProfile.MaxJobTitleLength, false)
                 })
        {
            var error = TextInput.Validate(value, field, min, max, required);
            if (error is not null) return Error.Validation("users.invalid_input", error);
        }

        var profile = await repository.GetByIdAsync(command.UserId, ct);
        if (profile is null) return Error.NotFound("users.not_found", "Usuario no encontrado.");

        try
        {
            profile.UpdateProfile(command.FullName, command.Department, command.JobTitle, timeProvider.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Code, ex.Message);
        }

        await repository.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.UserProfile(profile.Id), ct);
        await PublishAsync(EventTypes.UserUpdated, profile, command.Actor, ct, new Dictionary<string, string>
        {
            ["fullName"] = profile.FullName,
            ["department"] = profile.Department ?? string.Empty,
            ["jobTitle"] = profile.JobTitle ?? string.Empty
        });
        return Result<UserDto>.Success(UserDto.From(profile));
    }

    public Task<Result<UserDto>> DeactivateAsync(Guid userId, Actor actor, CancellationToken ct) =>
        ChangeStatusAsync(userId, actor, deactivate: true, ct);

    public Task<Result<UserDto>> ActivateAsync(Guid userId, Actor actor, CancellationToken ct) =>
        ChangeStatusAsync(userId, actor, deactivate: false, ct);

    private async Task<Result<UserDto>> ChangeStatusAsync(Guid userId, Actor actor, bool deactivate, CancellationToken ct)
    {
        if (!actor.CanWriteUsers) return Error.Forbidden("users.forbidden", "No tienes permiso para cambiar el estado de usuarios.");
        if (deactivate && actor.UserId == userId)
            return Error.Validation("users.self_deactivation", "No puedes desactivar tu propia cuenta.");

        var profile = await repository.GetByIdAsync(userId, ct);
        if (profile is null) return Error.NotFound("users.not_found", "Usuario no encontrado.");

        try
        {
            if (deactivate) profile.Deactivate(timeProvider.GetUtcNow());
            else profile.Activate(timeProvider.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Error.Conflict(ex.Code, ex.Message);
        }

        await repository.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.UserProfile(userId), ct);
        await PublishAsync(deactivate ? EventTypes.UserDeactivated : EventTypes.UserActivated, profile, actor, ct);
        return Result<UserDto>.Success(UserDto.From(profile));
    }

    /// <summary>Crea el perfil a partir de identity.user.registered. Idempotente (at-least-once).</summary>
    public async Task<bool> CreateFromRegistrationAsync(Guid userId, string email, string fullName, string? department, CancellationToken ct)
    {
        if (await repository.ExistsAsync(userId, ct)) return false;
        var profile = UserProfile.Create(userId, email, fullName, department, timeProvider.GetUtcNow());
        await repository.AddAsync(profile, ct);
        await repository.SaveChangesAsync(ct);
        return true;
    }

    private Task PublishAsync(string eventType, UserProfile profile, Actor actor, CancellationToken ct, Dictionary<string, string>? data = null) =>
        eventPublisher.PublishAsync(new IntegrationEvent
        {
            EventType = eventType,
            Source = "user-service",
            ActorId = actor.UserId?.ToString(),
            ActorEmail = actor.Email,
            SubjectId = profile.Id.ToString(),
            Data = data ?? new Dictionary<string, string> { ["email"] = profile.Email }
        }, ct);
}
