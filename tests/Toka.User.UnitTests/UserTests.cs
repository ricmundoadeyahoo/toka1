using Microsoft.Extensions.Logging.Abstractions;
using Toka.BuildingBlocks.Contracts.Caching;
using Toka.BuildingBlocks.Contracts.Events;
using Toka.BuildingBlocks.Contracts.Results;
using Toka.Tests.Shared;
using Toka.User.Application;
using Toka.User.Domain;

namespace Toka.User.UnitTests;

internal sealed class InMemoryUserRepository : IUserRepository
{
    public List<UserProfile> Items { get; } = [];
    public int GetByIdCalls { get; private set; }

    public Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        GetByIdCalls++;
        return Task.FromResult(Items.FirstOrDefault(u => u.Id == id));
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.Any(u => u.Id == id));

    public Task<(IReadOnlyList<UserProfile> Items, long Total)> SearchAsync(UserSearchCriteria c, CancellationToken ct)
    {
        IEnumerable<UserProfile> q = Items;
        if (c.Search is not null) q = q.Where(u => u.Email.Contains(c.Search, StringComparison.OrdinalIgnoreCase) || u.FullName.Contains(c.Search, StringComparison.OrdinalIgnoreCase));
        if (c.Status is not null) q = q.Where(u => u.Status == c.Status);
        if (c.Department is not null) q = q.Where(u => u.Department == c.Department);
        var list = q.ToList();
        return Task.FromResult<(IReadOnlyList<UserProfile>, long)>((list.Skip((c.Page - 1) * c.PageSize).Take(c.PageSize).ToList(), list.Count));
    }

    public Task<UserStatsDto> GetStatsAsync(CancellationToken ct) =>
        Task.FromResult(new UserStatsDto(Items.Count, Items.Count(u => u.IsActive), Items.Count(u => !u.IsActive),
            Items.GroupBy(u => u.Department ?? "-").ToDictionary(g => g.Key, g => (long)g.Count())));

    public Task AddAsync(UserProfile profile, CancellationToken ct)
    {
        Items.Add(profile);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

public class UserProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normaliza_y_queda_activo()
    {
        var p = UserProfile.Create(Guid.NewGuid(), " Ana@Toka.com ", " Ana López ", "  ", Now);
        Assert.Equal("ana@toka.com", p.Email);
        Assert.Equal("Ana López", p.FullName);
        Assert.Null(p.Department);
        Assert.True(p.IsActive);
        Assert.Equal(UserStatus.Active, p.Status);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "a@toka.com", "Ana")]
    [InlineData("11111111-1111-1111-1111-111111111111", "no-email", "Ana")]
    [InlineData("11111111-1111-1111-1111-111111111111", "a@toka.com", "A")]
    public void Create_valida_invariantes(string id, string email, string name) =>
        Assert.Throws<DomainException>(() => UserProfile.Create(Guid.Parse(id), email, name, null, Now));

    [Fact]
    public void UpdateProfile_valida_longitudes()
    {
        var p = UserProfile.Create(Guid.NewGuid(), "a@toka.com", "Ana", null, Now);
        p.UpdateProfile("Ana María", "Finanzas", "Analista", Now.AddDays(1));
        Assert.Equal("Analista", p.JobTitle);
        Assert.Equal(Now.AddDays(1), p.UpdatedAt);
        Assert.Throws<DomainException>(() => p.UpdateProfile("Ana", new string('x', 81), null, Now));
        Assert.Throws<DomainException>(() => p.UpdateProfile("Ana", null, new string('x', 81), Now));
    }

    [Fact]
    public void Transiciones_de_estado()
    {
        var p = UserProfile.Create(Guid.NewGuid(), "a@toka.com", "Ana", null, Now);
        Assert.Throws<DomainException>(() => p.Activate(Now));
        p.Deactivate(Now);
        Assert.Equal(UserStatus.Deactivated, p.Status);
        Assert.Throws<DomainException>(() => p.Deactivate(Now));
        p.Activate(Now);
        Assert.True(p.IsActive);
    }
}

public class UserUseCaseTests
{
    private readonly InMemoryUserRepository _repo = new();
    private readonly InMemoryCacheService _cache = new();
    private readonly RecordingEventPublisher _events = new();
    private readonly FixedTimeProvider _clock = FixedTimeProvider.Default();

    private UserQueries Queries => new(_repo, _cache);
    private UserCommands Commands => new(_repo, _cache, _events, _clock);

    private static readonly Actor Admin = new(Guid.NewGuid(), "admin@toka.com", CanWriteUsers: true, CanReadUsers: true);

    private UserProfile Seed(string email = "ana@toka.com", string name = "Ana López", string? dept = "Finanzas")
    {
        var p = UserProfile.Create(Guid.NewGuid(), email, name, dept, _clock.GetUtcNow());
        _repo.Items.Add(p);
        return p;
    }

    [Fact]
    public async Task GetById_usa_cache_aside()
    {
        var p = Seed();
        var first = await Queries.GetByIdAsync(p.Id, Admin, default);
        var second = await Queries.GetByIdAsync(p.Id, Admin, default);
        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(1, _repo.GetByIdCalls); // la segunda vino de Redis
        Assert.True(_cache.Store.ContainsKey(CacheKeys.UserProfile(p.Id)));
    }

    [Fact]
    public async Task GetById_permite_al_propio_usuario_y_bloquea_a_otros()
    {
        var p = Seed();
        var self = new Actor(p.Id, p.Email, false, false);
        var other = new Actor(Guid.NewGuid(), "x@toka.com", false, false);
        Assert.True((await Queries.GetByIdAsync(p.Id, self, default)).IsSuccess);
        Assert.Equal(ErrorType.Forbidden, (await Queries.GetByIdAsync(p.Id, other, default)).Error!.Type);
    }

    [Fact]
    public async Task GetById_inexistente() =>
        Assert.Equal(ErrorType.NotFound, (await Queries.GetByIdAsync(Guid.NewGuid(), Admin, default)).Error!.Type);

    [Fact]
    public async Task Search_filtra_y_pagina()
    {
        Seed("ana@toka.com", "Ana López");
        Seed("beto@toka.com", "Beto Ruiz", "TI");
        Seed("carla@toka.com", "Carla Díaz", "TI").Deactivate(_clock.GetUtcNow());

        var all = await Queries.SearchAsync(null, null, null, 1, 2, default);
        Assert.Equal(3, all.Value.TotalCount);
        Assert.Equal(2, all.Value.Items.Count);
        Assert.Equal(2, all.Value.TotalPages);

        var active = await Queries.SearchAsync(null, "active", "TI", null, null, default);
        Assert.Single(active.Value.Items);

        var byText = await Queries.SearchAsync("  beto ", null, null, null, null, default);
        Assert.Equal("beto@toka.com", byText.Value.Items.Single().Email);
    }

    [Theory]
    [InlineData("<script>", null)]
    [InlineData(null, "borrado")]
    [InlineData(null, "99")]
    public async Task Search_valida_parametros(string? search, string? status) =>
        Assert.Equal(ErrorType.Validation, (await Queries.SearchAsync(search, status, null, 1, 10, default)).Error!.Type);

    [Fact]
    public async Task Stats_delegan_al_repositorio()
    {
        Seed();
        var stats = await Queries.GetStatsAsync(default);
        Assert.Equal(1, stats.Active);
    }

    [Fact]
    public async Task Update_por_admin_invalida_cache_y_publica_evento()
    {
        var p = Seed();
        await Queries.GetByIdAsync(p.Id, Admin, default); // llena caché

        var result = await Commands.UpdateAsync(new UpdateUserCommand(p.Id, "Ana María López", "Riesgos", "Líder", Admin), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Riesgos", result.Value.Department);
        Assert.False(_cache.Store.ContainsKey(CacheKeys.UserProfile(p.Id)));
        var evt = _events.Single(EventTypes.UserUpdated);
        Assert.Equal(Admin.UserId.ToString(), evt.ActorId);
        Assert.Equal("Líder", evt.Data["jobTitle"]);
    }

    [Fact]
    public async Task Update_propio_perfil_sin_permiso_de_escritura()
    {
        var p = Seed();
        var self = new Actor(p.Id, p.Email, false, false);
        Assert.True((await Commands.UpdateAsync(new UpdateUserCommand(p.Id, "Ana L.", null, null, self), default)).IsSuccess);

        var other = new Actor(Guid.NewGuid(), "x@toka.com", false, false);
        Assert.Equal(ErrorType.Forbidden, (await Commands.UpdateAsync(new UpdateUserCommand(p.Id, "Hack", null, null, other), default)).Error!.Type);
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("<b>Ana</b>", null, null)]
    [InlineData("Ana", "<img>", null)]
    public async Task Update_valida_entradas(string name, string? dept, string? title)
    {
        var p = Seed();
        var result = await Commands.UpdateAsync(new UpdateUserCommand(p.Id, name, dept, title, Admin), default);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task Update_inexistente() =>
        Assert.Equal(ErrorType.NotFound, (await Commands.UpdateAsync(new UpdateUserCommand(Guid.NewGuid(), "Ana", null, null, Admin), default)).Error!.Type);

    [Fact]
    public async Task Desactivar_y_activar_publica_eventos()
    {
        var p = Seed();
        Assert.True((await Commands.DeactivateAsync(p.Id, Admin, default)).IsSuccess);
        Assert.True(_events.Contains(EventTypes.UserDeactivated));
        Assert.Equal(ErrorType.Conflict, (await Commands.DeactivateAsync(p.Id, Admin, default)).Error!.Type);

        Assert.True((await Commands.ActivateAsync(p.Id, Admin, default)).IsSuccess);
        Assert.True(_events.Contains(EventTypes.UserActivated));
    }

    [Fact]
    public async Task Desactivar_reglas_de_autorizacion()
    {
        var p = Seed();
        Assert.Equal(ErrorType.Forbidden, (await Commands.DeactivateAsync(p.Id, new Actor(p.Id, p.Email, false, true), default)).Error!.Type);
        Assert.Equal("users.self_deactivation", (await Commands.DeactivateAsync(Admin.UserId!.Value, Admin, default)).Error!.Code);
        Assert.Equal(ErrorType.NotFound, (await Commands.DeactivateAsync(Guid.NewGuid(), Admin, default)).Error!.Type);
    }

    [Fact]
    public async Task Handler_crea_perfil_de_forma_idempotente()
    {
        var handler = new UserRegisteredHandler(Commands, NullLogger<UserRegisteredHandler>.Instance);
        var userId = Guid.NewGuid();
        var evt = new IntegrationEvent
        {
            EventType = EventTypes.UserRegistered, Source = "auth-service", SubjectId = userId.ToString(),
            Data = new() { ["email"] = "nuevo@toka.com", ["fullName"] = "Nuevo Usuario", ["department"] = "TI" }
        };

        await handler.HandleAsync(evt, default);
        await handler.HandleAsync(evt, default); // reentrega

        Assert.Single(_repo.Items);
        Assert.Equal("TI", _repo.Items[0].Department);
        Assert.Contains(EventTypes.UserRegistered, handler.EventTypes);
    }

    [Fact]
    public async Task Handler_rechaza_evento_incompleto_para_enviarlo_a_DLQ()
    {
        var handler = new UserRegisteredHandler(Commands, NullLogger<UserRegisteredHandler>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new IntegrationEvent { EventType = EventTypes.UserRegistered, Source = "x", SubjectId = Guid.NewGuid().ToString() }, default));
    }
}
