using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Toka.BuildingBlocks.Contracts.Caching;
using Toka.BuildingBlocks.Contracts.Events;
using Toka.BuildingBlocks.Contracts.Results;
using Toka.BuildingBlocks.Contracts.Security;
using Toka.Roles.Domain;

namespace Toka.Roles.Application;

public interface IRoleRepository
{
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct);
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Role?> GetByNameAsync(string name, CancellationToken ct);
    Task<IReadOnlyList<Role>> GetRolesForUserAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetUserIdsWithRoleAsync(Guid roleId, CancellationToken ct);
    Task<UserRoleAssignment?> GetAssignmentAsync(Guid userId, Guid roleId, CancellationToken ct);
    Task<int> CountUsersWithRoleAsync(Guid roleId, CancellationToken ct);
    Task AddRoleAsync(Role role, CancellationToken ct);
    void RemoveRole(Role role);
    Task AddAssignmentAsync(UserRoleAssignment assignment, CancellationToken ct);
    void RemoveAssignment(UserRoleAssignment assignment);
    Task SaveChangesAsync(CancellationToken ct);
}

public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions)
{
    public static RoleDto From(Role r) => new(r.Id, r.Name, r.Description, r.IsSystem, r.Permissions);
}

public sealed record UserRolesDto(Guid UserId, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed record CreateRoleCommand(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record RoleActor(Guid? UserId, string? Email);

public sealed class RoleUseCases(IRoleRepository repository, ICacheService cache, IEventPublisher eventPublisher, TimeProvider timeProvider)
{
    internal static readonly TimeSpan UserRolesTtl = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct) =>
        (await repository.ListAsync(ct)).Select(RoleDto.From).ToList();

    /// <summary>Roles + permisos efectivos (unión) de un usuario. Cache-aside en Redis.</summary>
    public async Task<UserRolesDto> GetUserRolesAsync(Guid userId, CancellationToken ct)
    {
        var cached = await cache.GetAsync<UserRolesDto>(CacheKeys.UserRoles(userId), ct);
        if (cached is not null) return cached;

        var roles = await repository.GetRolesForUserAsync(userId, ct);
        var dto = new UserRolesDto(
            userId,
            roles.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToList(),
            roles.SelectMany(r => r.Permissions).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList());
        await cache.SetAsync(CacheKeys.UserRoles(userId), dto, UserRolesTtl, ct);
        return dto;
    }

    public async Task<Result<RoleDto>> CreateAsync(CreateRoleCommand command, RoleActor actor, CancellationToken ct)
    {
        Role role;
        try
        {
            role = Role.Create(Guid.NewGuid(), command.Name, command.Description, command.Permissions ?? Array.Empty<string>(), isSystem: false, timeProvider.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Code, ex.Message);
        }

        if (await repository.GetByNameAsync(role.Name, ct) is not null)
            return Error.Conflict("roles.duplicate", $"Ya existe el rol '{role.Name}'.");

        await repository.AddRoleAsync(role, ct);
        await repository.SaveChangesAsync(ct);
        await PublishAsync(EventTypes.RoleCreated, actor, role.Id.ToString(), new() { ["role"] = role.Name, ["permissions"] = string.Join(",", role.Permissions) }, ct);
        return Result<RoleDto>.Success(RoleDto.From(role));
    }

    public async Task<Result<RoleDto>> UpdatePermissionsAsync(Guid roleId, IReadOnlyList<string> permissions, RoleActor actor, CancellationToken ct)
    {
        var role = await repository.GetByIdAsync(roleId, ct);
        if (role is null) return Error.NotFound("roles.not_found", "Rol no encontrado.");
        if (role.Name == SystemRoles.Admin)
            return Error.Validation("roles.admin_locked", "Los permisos del rol Admin no se pueden modificar.");

        try
        {
            role.ReplacePermissions(permissions ?? Array.Empty<string>());
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Code, ex.Message);
        }

        await repository.SaveChangesAsync(ct);
        await InvalidateUsersOfRoleAsync(role.Id, ct);
        await PublishAsync(EventTypes.RolePermissionsChanged, actor, role.Id.ToString(), new() { ["role"] = role.Name, ["permissions"] = string.Join(",", role.Permissions) }, ct);
        return Result<RoleDto>.Success(RoleDto.From(role));
    }

    public async Task<Result> DeleteAsync(Guid roleId, RoleActor actor, CancellationToken ct)
    {
        var role = await repository.GetByIdAsync(roleId, ct);
        if (role is null) return Error.NotFound("roles.not_found", "Rol no encontrado.");
        try
        {
            role.EnsureCanBeDeleted();
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Code, ex.Message);
        }
        if (await repository.CountUsersWithRoleAsync(role.Id, ct) > 0)
            return Error.Conflict("roles.in_use", "El rol tiene usuarios asignados; revoca las asignaciones primero.");

        repository.RemoveRole(role);
        await repository.SaveChangesAsync(ct);
        await PublishAsync(EventTypes.RoleDeleted, actor, role.Id.ToString(), new() { ["role"] = role.Name }, ct);
        return Result.Success();
    }

    public async Task<Result<UserRolesDto>> AssignAsync(Guid userId, string roleName, RoleActor actor, CancellationToken ct)
    {
        if (userId == Guid.Empty) return Error.Validation("roles.user_invalid", "Usuario inválido.");
        var role = await repository.GetByNameAsync(roleName?.Trim() ?? string.Empty, ct);
        if (role is null) return Error.NotFound("roles.not_found", $"El rol '{roleName}' no existe.");
        if (await repository.GetAssignmentAsync(userId, role.Id, ct) is not null)
            return Error.Conflict("roles.already_assigned", $"El usuario ya tiene el rol '{role.Name}'.");

        await repository.AddAssignmentAsync(UserRoleAssignment.Create(userId, role, actor.Email ?? actor.UserId?.ToString(), timeProvider.GetUtcNow()), ct);
        await repository.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.UserRoles(userId), ct);
        await PublishAsync(EventTypes.RoleAssigned, actor, userId.ToString(), new() { ["role"] = role.Name }, ct);
        return Result<UserRolesDto>.Success(await GetUserRolesAsync(userId, ct));
    }

    public async Task<Result<UserRolesDto>> RevokeAsync(Guid userId, string roleName, RoleActor actor, CancellationToken ct)
    {
        var role = await repository.GetByNameAsync(roleName?.Trim() ?? string.Empty, ct);
        if (role is null) return Error.NotFound("roles.not_found", $"El rol '{roleName}' no existe.");
        var assignment = await repository.GetAssignmentAsync(userId, role.Id, ct);
        if (assignment is null) return Error.NotFound("roles.not_assigned", $"El usuario no tiene el rol '{role.Name}'.");

        // Invariante de negocio: siempre debe existir al menos un administrador.
        if (role.Name == SystemRoles.Admin && await repository.CountUsersWithRoleAsync(role.Id, ct) <= 1)
            return Error.Conflict("roles.last_admin", "No se puede revocar el último administrador del sistema.");

        repository.RemoveAssignment(assignment);
        await repository.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.UserRoles(userId), ct);
        await PublishAsync(EventTypes.RoleRevoked, actor, userId.ToString(), new() { ["role"] = role.Name }, ct);
        return Result<UserRolesDto>.Success(await GetUserRolesAsync(userId, ct));
    }

    /// <summary>Asigna el rol por defecto a un usuario recién registrado (idempotente).</summary>
    public async Task<bool> AssignDefaultRoleAsync(Guid userId, CancellationToken ct)
    {
        var role = await repository.GetByNameAsync(SystemRoles.User, ct)
                   ?? throw new InvalidOperationException("El rol por defecto 'User' no existe.");
        if (await repository.GetAssignmentAsync(userId, role.Id, ct) is not null) return false;

        await repository.AddAssignmentAsync(UserRoleAssignment.Create(userId, role, "system", timeProvider.GetUtcNow()), ct);
        await repository.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.UserRoles(userId), ct);
        await PublishAsync(EventTypes.RoleAssigned, new RoleActor(null, "system"), userId.ToString(), new() { ["role"] = role.Name, ["reason"] = "default" }, ct);
        return true;
    }

    private async Task InvalidateUsersOfRoleAsync(Guid roleId, CancellationToken ct)
    {
        foreach (var userId in await repository.GetUserIdsWithRoleAsync(roleId, ct))
            await cache.RemoveAsync(CacheKeys.UserRoles(userId), ct);
    }

    private Task PublishAsync(string eventType, RoleActor actor, string subjectId, Dictionary<string, string> data, CancellationToken ct) =>
        eventPublisher.PublishAsync(new IntegrationEvent
        {
            EventType = eventType,
            Source = "role-service",
            ActorId = actor.UserId?.ToString(),
            ActorEmail = actor.Email,
            SubjectId = subjectId,
            Data = data
        }, ct);
}

/// <summary>Consume identity.user.registered → asigna rol "User".</summary>
public sealed class AssignDefaultRoleHandler(RoleUseCases useCases, ILogger<AssignDefaultRoleHandler> logger) : IIntegrationEventHandler
{
    public IReadOnlyCollection<string> EventTypes { get; } = [global::Toka.BuildingBlocks.Contracts.Events.EventTypes.UserRegistered];

    public async Task HandleAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(integrationEvent.SubjectId, out var userId))
            throw new InvalidOperationException($"Evento {integrationEvent.Id} sin SubjectId válido.");
        var assigned = await useCases.AssignDefaultRoleAsync(userId, cancellationToken);
        logger.LogInformation("Rol por defecto para {UserId}: {Assigned}", userId, assigned ? "asignado" : "ya existía");
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddRolesApplication(this IServiceCollection services)
    {
        services.AddScoped<RoleUseCases>();
        return services;
    }
}
