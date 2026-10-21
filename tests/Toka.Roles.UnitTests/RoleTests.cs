using Microsoft.Extensions.Logging.Abstractions;
using Toka.BuildingBlocks.Contracts.Caching;
using Toka.BuildingBlocks.Contracts.Events;
using Toka.BuildingBlocks.Contracts.Results;
using Toka.BuildingBlocks.Contracts.Security;
using Toka.Roles.Application;
using Toka.Roles.Domain;
using Toka.Tests.Shared;

namespace Toka.Roles.UnitTests;

internal sealed class InMemoryRoleRepository : IRoleRepository
{
    public List<Role> RoleItems { get; } = [];
    public List<UserRoleAssignment> AssignmentItems { get; } = [];

    public Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Role>>(RoleItems.OrderBy(r => r.Name).ToList());
    public Task<Role?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(RoleItems.FirstOrDefault(r => r.Id == id));
    public Task<Role?> GetByNameAsync(string name, CancellationToken ct) =>
        Task.FromResult(RoleItems.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)));
    public Task<IReadOnlyList<Role>> GetRolesForUserAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Role>>(AssignmentItems.Where(a => a.UserId == userId).Select(a => RoleItems.First(r => r.Id == a.RoleId)).ToList());
    public Task<IReadOnlyList<Guid>> GetUserIdsWithRoleAsync(Guid roleId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>(AssignmentItems.Where(a => a.RoleId == roleId).Select(a => a.UserId).ToList());
    public Task<UserRoleAssignment?> GetAssignmentAsync(Guid userId, Guid roleId, CancellationToken ct) =>
        Task.FromResult(AssignmentItems.FirstOrDefault(a => a.UserId == userId && a.RoleId == roleId));
    public Task<int> CountUsersWithRoleAsync(Guid roleId, CancellationToken ct) => Task.FromResult(AssignmentItems.Count(a => a.RoleId == roleId));
    public Task AddRoleAsync(Role role, CancellationToken ct) { RoleItems.Add(role); return Task.CompletedTask; }
    public void RemoveRole(Role role) => RoleItems.Remove(role);
    public Task AddAssignmentAsync(UserRoleAssignment assignment, CancellationToken ct) { AssignmentItems.Add(assignment); return Task.CompletedTask; }
    public void RemoveAssignment(UserRoleAssignment assignment) => AssignmentItems.Remove(assignment);
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

public class RoleDomainTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Create_normaliza_permisos_sin_duplicados_y_ordenados()
    {
        var role = Role.Create(Guid.Empty, " Soporte ", " Mesa de ayuda ", ["Users:Read", "users:read", "audit:read"], false, Now);
        Assert.NotEqual(Guid.Empty, role.Id);
        Assert.Equal("Soporte", role.Name);
        Assert.Equal("Mesa de ayuda", role.Description);
        Assert.Equal(new[] { "audit:read", "users:read" }, role.Permissions);
    }

    [Theory]
    [InlineData("1rol")]
    [InlineData("a")]
    [InlineData("rol con espacios")]
    [InlineData("")]
    public void Create_valida_nombre(string name) =>
        Assert.Equal("role.name_invalid", Assert.Throws<DomainException>(() => Role.Create(Guid.NewGuid(), name, null, [], false, Now)).Code);

    [Fact]
    public void Create_valida_descripcion() =>
        Assert.Throws<DomainException>(() => Role.Create(Guid.NewGuid(), "Rol", new string('x', 201), [], false, Now));

    [Theory]
    [InlineData("users")]
    [InlineData("users:")]
    [InlineData("USERS READ")]
    [InlineData("users:read;drop")]
    public void ReplacePermissions_rechaza_formato_invalido(string permission)
    {
        var role = Role.Create(Guid.NewGuid(), "Rol", null, [], false, Now);
        Assert.Equal("role.permission_invalid", Assert.Throws<DomainException>(() => role.ReplacePermissions([permission])).Code);
    }

    [Fact]
    public void ReplacePermissions_limita_cantidad()
    {
        var role = Role.Create(Guid.NewGuid(), "Rol", null, [], false, Now);
        var many = Enumerable.Range(0, 51).Select(i => $"res{i:00}:read");
        Assert.Equal("role.permission_limit", Assert.Throws<DomainException>(() => role.ReplacePermissions(many)).Code);
    }

    [Fact]
    public void Rol_de_sistema_no_se_elimina()
    {
        Assert.Throws<DomainException>(() => Role.Create(Guid.NewGuid(), "Admin", null, [], true, Now).EnsureCanBeDeleted());
        Role.Create(Guid.NewGuid(), "Temporal", null, [], false, Now).EnsureCanBeDeleted();
    }

    [Fact]
    public void Asignacion_requiere_usuario()
    {
        var role = Role.Create(Guid.NewGuid(), "Rol", null, [], false, Now);
        Assert.Throws<DomainException>(() => UserRoleAssignment.Create(Guid.Empty, role, "x", Now));
        var a = UserRoleAssignment.Create(Guid.NewGuid(), role, "admin", Now);
        Assert.Equal(role.Id, a.RoleId);
        Assert.Equal("admin", a.AssignedBy);
    }
}

public class RoleUseCaseTests
{
    private readonly InMemoryRoleRepository _repo = new();
    private readonly InMemoryCacheService _cache = new();
    private readonly RecordingEventPublisher _events = new();
    private readonly FixedTimeProvider _clock = FixedTimeProvider.Default();
    private readonly RoleActor _actor = new(Guid.NewGuid(), "admin@toka.com");
    private RoleUseCases UseCases => new(_repo, _cache, _events, _clock);

    public RoleUseCaseTests()
    {
        _repo.RoleItems.Add(Role.Create(Guid.NewGuid(), SystemRoles.Admin, null, Permissions.All, true, _clock.GetUtcNow()));
        _repo.RoleItems.Add(Role.Create(Guid.NewGuid(), SystemRoles.User, null, [Permissions.AiUse], true, _clock.GetUtcNow()));
        _repo.RoleItems.Add(Role.Create(Guid.NewGuid(), SystemRoles.Auditor, null, [Permissions.AuditRead, Permissions.UsersRead], true, _clock.GetUtcNow()));
    }

    private Role RoleNamed(string name) => _repo.RoleItems.Single(r => r.Name == name);

    [Fact]
    public async Task List_devuelve_roles()
    {
        var roles = await UseCases.ListAsync(default);
        Assert.Equal(3, roles.Count);
        Assert.True(roles.All(r => r.IsSystem));
    }

    [Fact]
    public async Task Crear_rol_personalizado_y_detectar_duplicado()
    {
        var created = await UseCases.CreateAsync(new CreateRoleCommand("Soporte", "Mesa", ["users:read"]), _actor, default);
        Assert.True(created.IsSuccess);
        Assert.True(_events.Contains(EventTypes.RoleCreated));

        var duplicate = await UseCases.CreateAsync(new CreateRoleCommand("soporte", null, []), _actor, default);
        Assert.Equal(ErrorType.Conflict, duplicate.Error!.Type);

        var invalid = await UseCases.CreateAsync(new CreateRoleCommand("x", null, []), _actor, default);
        Assert.Equal(ErrorType.Validation, invalid.Error!.Type);
    }

    [Fact]
    public async Task Asignar_rol_calcula_permisos_efectivos_e_invalida_cache()
    {
        var userId = Guid.NewGuid();
        await UseCases.GetUserRolesAsync(userId, default); // cachea "sin roles"

        var result = await UseCases.AssignAsync(userId, "auditor", _actor, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "Auditor" }, result.Value.Roles);
        Assert.Equal(new[] { "audit:read", "users:read" }, result.Value.Permissions);
        Assert.Equal("Auditor", _events.Single(EventTypes.RoleAssigned).Data["role"]);

        await UseCases.AssignAsync(userId, SystemRoles.User, _actor, default);
        var merged = await UseCases.GetUserRolesAsync(userId, default);
        Assert.Equal(new[] { "ai:use", "audit:read", "users:read" }, merged.Permissions);
    }

    [Fact]
    public async Task Asignar_errores()
    {
        var userId = Guid.NewGuid();
        Assert.Equal(ErrorType.Validation, (await UseCases.AssignAsync(Guid.Empty, "User", _actor, default)).Error!.Type);
        Assert.Equal(ErrorType.NotFound, (await UseCases.AssignAsync(userId, "NoExiste", _actor, default)).Error!.Type);
        await UseCases.AssignAsync(userId, "User", _actor, default);
        Assert.Equal(ErrorType.Conflict, (await UseCases.AssignAsync(userId, "User", _actor, default)).Error!.Type);
    }

    [Fact]
    public async Task Revocar_rol_y_proteger_ultimo_admin()
    {
        var admin1 = Guid.NewGuid();
        var admin2 = Guid.NewGuid();
        await UseCases.AssignAsync(admin1, "Admin", _actor, default);

        var blocked = await UseCases.RevokeAsync(admin1, "Admin", _actor, default);
        Assert.Equal("roles.last_admin", blocked.Error!.Code);

        await UseCases.AssignAsync(admin2, "Admin", _actor, default);
        var revoked = await UseCases.RevokeAsync(admin1, "Admin", _actor, default);
        Assert.True(revoked.IsSuccess);
        Assert.Empty(revoked.Value.Roles);
        Assert.True(_events.Contains(EventTypes.RoleRevoked));

        Assert.Equal(ErrorType.NotFound, (await UseCases.RevokeAsync(admin1, "Admin", _actor, default)).Error!.Type);
        Assert.Equal(ErrorType.NotFound, (await UseCases.RevokeAsync(admin1, "Fantasma", _actor, default)).Error!.Type);
    }

    [Fact]
    public async Task Actualizar_permisos_invalida_cache_de_usuarios_del_rol()
    {
        var userId = Guid.NewGuid();
        await UseCases.AssignAsync(userId, "Auditor", _actor, default);
        await UseCases.GetUserRolesAsync(userId, default);
        Assert.True(_cache.Store.ContainsKey(CacheKeys.UserRoles(userId)));

        var result = await UseCases.UpdatePermissionsAsync(RoleNamed("Auditor").Id, ["audit:read"], _actor, default);

        Assert.True(result.IsSuccess);
        Assert.False(_cache.Store.ContainsKey(CacheKeys.UserRoles(userId)));
        Assert.True(_events.Contains(EventTypes.RolePermissionsChanged));
    }

    [Fact]
    public async Task Actualizar_permisos_errores()
    {
        Assert.Equal(ErrorType.NotFound, (await UseCases.UpdatePermissionsAsync(Guid.NewGuid(), [], _actor, default)).Error!.Type);
        Assert.Equal("roles.admin_locked", (await UseCases.UpdatePermissionsAsync(RoleNamed("Admin").Id, [], _actor, default)).Error!.Code);
        Assert.Equal(ErrorType.Validation, (await UseCases.UpdatePermissionsAsync(RoleNamed("User").Id, ["malo"], _actor, default)).Error!.Type);
    }

    [Fact]
    public async Task Eliminar_rol()
    {
        var created = await UseCases.CreateAsync(new CreateRoleCommand("Temporal", null, []), _actor, default);
        var userId = Guid.NewGuid();
        await UseCases.AssignAsync(userId, "Temporal", _actor, default);

        Assert.Equal("roles.in_use", (await UseCases.DeleteAsync(created.Value.Id, _actor, default)).Error!.Code);
        await UseCases.RevokeAsync(userId, "Temporal", _actor, default);
        Assert.True((await UseCases.DeleteAsync(created.Value.Id, _actor, default)).IsSuccess);
        Assert.True(_events.Contains(EventTypes.RoleDeleted));

        Assert.Equal(ErrorType.Validation, (await UseCases.DeleteAsync(RoleNamed("User").Id, _actor, default)).Error!.Type);
        Assert.Equal(ErrorType.NotFound, (await UseCases.DeleteAsync(Guid.NewGuid(), _actor, default)).Error!.Type);
    }

    [Fact]
    public async Task Handler_asigna_rol_por_defecto_idempotente()
    {
        var handler = new AssignDefaultRoleHandler(UseCases, NullLogger<AssignDefaultRoleHandler>.Instance);
        var userId = Guid.NewGuid();
        var evt = new IntegrationEvent { EventType = EventTypes.UserRegistered, Source = "auth-service", SubjectId = userId.ToString() };

        await handler.HandleAsync(evt, default);
        await handler.HandleAsync(evt, default);

        Assert.Single(_repo.AssignmentItems);
        Assert.Equal(new[] { "User" }, (await UseCases.GetUserRolesAsync(userId, default)).Roles);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new IntegrationEvent { EventType = EventTypes.UserRegistered, Source = "x", SubjectId = "?" }, default));
    }
}
