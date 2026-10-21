using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Toka.Auth.Application;
using Toka.Auth.Application.EventHandlers;
using Toka.Auth.Application.UseCases;
using Toka.Auth.Domain;
using Toka.BuildingBlocks.Contracts.Events;
using Toka.BuildingBlocks.Contracts.Results;
using Toka.Tests.Shared;

namespace Toka.Auth.UnitTests;

public class AuthUseCaseTests
{
    private readonly InMemoryCredentialRepository _repo = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeTokenService _tokens = new();
    private readonly InMemoryRefreshTokenStore _refreshStore = new();
    private readonly FakeRoleServiceClient _roles = new();
    private readonly RecordingEventPublisher _events = new();
    private readonly FixedTimeProvider _clock = FixedTimeProvider.Default();
    private readonly IOptions<AuthOptions> _options = Options.Create(new AuthOptions());

    private TokenIssuer Issuer => new(_tokens, _refreshStore, _roles, _options, _clock);
    private RegisterUseCase Register => new(_repo, _hasher, _events, _clock, NullLogger<RegisterUseCase>.Instance);
    private LoginUseCase Login => new(_repo, _hasher, Issuer, _events, _options, _clock, NullLogger<LoginUseCase>.Instance);

    private UserCredential Seed(string email = "ana@toka.com", string password = "Segura#2026")
    {
        var c = UserCredential.Register(Guid.NewGuid(), EmailAddress.Create(email), _hasher.Hash(password), _clock.GetUtcNow());
        _repo.Items.Add(c);
        return c;
    }

    // ---------------- Register ----------------

    [Fact]
    public async Task Register_crea_credencial_y_publica_evento()
    {
        var result = await Register.ExecuteAsync(new RegisterCommand("Ana@Toka.com", "Segura#2026", " Ana López ", "Finanzas", "10.0.0.1"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("ana@toka.com", result.Value.Email);
        Assert.Single(_repo.Items);
        var evt = _events.Single(EventTypes.UserRegistered);
        Assert.Equal(result.Value.UserId.ToString(), evt.SubjectId);
        Assert.Equal("Ana López", evt.Data["fullName"]);
        Assert.Equal("Finanzas", evt.Data["department"]);
        Assert.Equal("10.0.0.1", evt.IpAddress);
    }

    [Fact]
    public async Task Register_sin_departamento_no_incluye_la_clave()
    {
        var result = await Register.ExecuteAsync(new RegisterCommand("b@toka.com", "Segura#2026", "Beto", null, null), default);
        Assert.True(result.IsSuccess);
        Assert.False(_events.Single(EventTypes.UserRegistered).Data.ContainsKey("department"));
    }

    [Theory]
    [InlineData("no-es-email", "Segura#2026", "Ana", "auth.email_invalid")]
    [InlineData("a@toka.com", "debil", "Ana", "auth.password_weak")]
    [InlineData("a@toka.com", "Segura#2026", "<script>", "auth.fullname_invalid")]
    [InlineData("a@toka.com", "Segura#2026", "A", "auth.fullname_invalid")]
    public async Task Register_valida_entradas(string email, string password, string name, string expectedCode)
    {
        var result = await Register.ExecuteAsync(new RegisterCommand(email, password, name, null, null), default);
        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(_events.Events);
    }

    [Fact]
    public async Task Register_valida_departamento()
    {
        var result = await Register.ExecuteAsync(new RegisterCommand("a@toka.com", "Segura#2026", "Ana", "<b>x</b>", null), default);
        Assert.Equal("auth.department_invalid", result.Error!.Code);
    }

    [Fact]
    public async Task Register_rechaza_email_duplicado()
    {
        Seed("dup@toka.com");
        var result = await Register.ExecuteAsync(new RegisterCommand("DUP@toka.com", "Segura#2026", "Otro", null, null), default);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    // ---------------- Login ----------------

    [Fact]
    public async Task Login_exitoso_emite_tokens_con_roles_y_publica_evento()
    {
        var user = Seed();
        _roles.Response = new UserAuthorization(["Admin"], ["users:read", "users:write"]);

        var result = await Login.ExecuteAsync(new LoginCommand("ana@toka.com", "Segura#2026", "1.2.3.4"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bearer", result.Value.TokenType);
        Assert.NotNull(result.Value.RefreshToken);
        Assert.Equal(900, result.Value.ExpiresIn);
        Assert.Equal(new[] { "Admin" }, result.Value.User!.Roles);
        Assert.Equal(user.Id.ToString(), _tokens.LastRequest!.Subject);
        Assert.Contains("users:write", _tokens.LastRequest.Permissions);
        Assert.True(_refreshStore.Tokens.ContainsKey(result.Value.RefreshToken!));
        Assert.Equal(_clock.GetUtcNow(), user.LastLoginAt);
        Assert.True(_events.Contains(EventTypes.LoginSucceeded));
    }

    [Fact]
    public async Task Login_usuario_inexistente_responde_generico_y_audita()
    {
        var result = await Login.ExecuteAsync(new LoginCommand("nadie@toka.com", "Segura#2026", null), default);
        Assert.Equal("auth.invalid_credentials", result.Error!.Code);
        Assert.Equal(1, _hasher.HashCalls); // tiempo homogéneo
        Assert.Equal("unknown_user", _events.Single(EventTypes.LoginFailed).Data["reason"]);
    }

    [Theory]
    [InlineData("invalido", "x")]
    [InlineData("ana@toka.com", "")]
    public async Task Login_con_entrada_invalida_falla_sin_consultar(string email, string password)
    {
        var result = await Login.ExecuteAsync(new LoginCommand(email, password, null), default);
        Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
    }

    [Fact]
    public async Task Login_password_incorrecto_incrementa_intentos()
    {
        var user = Seed();
        var result = await Login.ExecuteAsync(new LoginCommand("ana@toka.com", "Mala#2026", null), default);
        Assert.Equal("auth.invalid_credentials", result.Error!.Code);
        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.Equal("bad_password", _events.Single(EventTypes.LoginFailed).Data["reason"]);
    }

    [Fact]
    public async Task Login_bloquea_tras_cinco_fallos_y_luego_rechaza()
    {
        var user = Seed();
        for (var i = 0; i < 5; i++)
            await Login.ExecuteAsync(new LoginCommand("ana@toka.com", "Mala#2026", null), default);

        Assert.True(user.IsLockedOut(_clock.GetUtcNow()));
        Assert.True(_events.Contains(EventTypes.AccountLocked));

        var locked = await Login.ExecuteAsync(new LoginCommand("ana@toka.com", "Segura#2026", null), default);
        Assert.Equal("auth.locked_out", locked.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, locked.Error.Type);
    }

    [Fact]
    public async Task Login_cuenta_inactiva_es_rechazada()
    {
        Seed().Deactivate();
        var result = await Login.ExecuteAsync(new LoginCommand("ana@toka.com", "Segura#2026", null), default);
        Assert.Equal("auth.inactive", result.Error!.Code);
    }

    [Fact]
    public async Task Login_sin_role_service_devuelve_unavailable()
    {
        Seed();
        _roles.Response = null;
        var result = await Login.ExecuteAsync(new LoginCommand("ana@toka.com", "Segura#2026", null), default);
        Assert.Equal(ErrorType.Unavailable, result.Error!.Type);
        Assert.False(_events.Contains(EventTypes.LoginSucceeded));
    }

    // ---------------- Refresh / Logout ----------------

    [Fact]
    public async Task Refresh_rota_el_token()
    {
        var user = Seed();
        _refreshStore.Tokens["r1"] = new RefreshTokenEntry(user.Id, _clock.GetUtcNow().AddDays(1));
        var useCase = new RefreshTokenUseCase(_refreshStore, _repo, Issuer, _clock);

        var result = await useCase.ExecuteAsync(new RefreshCommand("r1", null), default);

        Assert.True(result.IsSuccess);
        Assert.False(_refreshStore.Tokens.ContainsKey("r1"));
        Assert.True(_refreshStore.Tokens.ContainsKey(result.Value.RefreshToken!));
        var reuse = await useCase.ExecuteAsync(new RefreshCommand("r1", null), default);
        Assert.Equal(ErrorType.Unauthorized, reuse.Error!.Type);
    }

    [Theory]
    [InlineData("")]
    [InlineData("desconocido")]
    public async Task Refresh_invalido(string token)
    {
        var useCase = new RefreshTokenUseCase(_refreshStore, _repo, Issuer, _clock);
        var result = await useCase.ExecuteAsync(new RefreshCommand(token, null), default);
        Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
    }

    [Fact]
    public async Task Refresh_expirado_o_usuario_inactivo()
    {
        var user = Seed();
        var useCase = new RefreshTokenUseCase(_refreshStore, _repo, Issuer, _clock);

        _refreshStore.Tokens["viejo"] = new RefreshTokenEntry(user.Id, _clock.GetUtcNow().AddSeconds(-1));
        Assert.Equal(ErrorType.Unauthorized, (await useCase.ExecuteAsync(new RefreshCommand("viejo", null), default)).Error!.Type);

        user.Deactivate();
        _refreshStore.Tokens["r2"] = new RefreshTokenEntry(user.Id, _clock.GetUtcNow().AddDays(1));
        Assert.Equal(ErrorType.Forbidden, (await useCase.ExecuteAsync(new RefreshCommand("r2", null), default)).Error!.Type);
    }

    [Fact]
    public async Task Logout_revoca_access_y_refresh()
    {
        var revocation = new Mock<ITokenRevocationStore>();
        _refreshStore.Tokens["r1"] = new RefreshTokenEntry(Guid.NewGuid(), _clock.GetUtcNow().AddDays(1));
        var useCase = new LogoutUseCase(revocation.Object, _refreshStore, _events);
        var userId = Guid.NewGuid();

        var result = await useCase.ExecuteAsync(new LogoutCommand(userId, "a@toka.com", "jti-1", _clock.GetUtcNow().AddMinutes(10), "r1"), default);

        Assert.True(result.IsSuccess);
        revocation.Verify(r => r.RevokeAccessTokenAsync("jti-1", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(_refreshStore.Tokens);
        Assert.Equal(userId.ToString(), _events.Single(EventTypes.LoggedOut).ActorId);
    }

    [Fact]
    public async Task Logout_sin_jti_es_invalido()
    {
        var useCase = new LogoutUseCase(Mock.Of<ITokenRevocationStore>(), _refreshStore, _events);
        var result = await useCase.ExecuteAsync(new LogoutCommand(Guid.NewGuid(), null, "", _clock.GetUtcNow(), null), default);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    // ---------------- client_credentials ----------------

    private ClientCredentialsUseCase ClientCredentials()
    {
        var registry = new Mock<IClientRegistry>();
        registry.Setup(r => r.Find("ai-service")).Returns(new OAuthClient("ai-service", "secreto-muy-largo-123", ["users.read", "roles.read"]));
        return new ClientCredentialsUseCase(registry.Object, _tokens, _clock);
    }

    [Fact]
    public async Task ClientCredentials_emite_token_con_scopes()
    {
        var result = await ClientCredentials().ExecuteAsync(new ClientCredentialsCommand("ai-service", "secreto-muy-largo-123", "users.read"), default);
        Assert.True(result.IsSuccess);
        Assert.Equal("users.read", result.Value.Scope);
        Assert.Null(result.Value.RefreshToken);
        Assert.Equal(new[] { "users.read" }, _tokens.LastRequest!.Scopes);
    }

    [Fact]
    public async Task ClientCredentials_sin_scope_concede_todos_los_permitidos()
    {
        var result = await ClientCredentials().ExecuteAsync(new ClientCredentialsCommand("ai-service", "secreto-muy-largo-123", null), default);
        Assert.Equal("users.read roles.read", result.Value.Scope);
    }

    [Theory]
    [InlineData("ai-service", "incorrecto", "users.read", "invalid_client")]
    [InlineData("otro", "secreto-muy-largo-123", null, "invalid_client")]
    [InlineData("", "x", null, "invalid_client")]
    [InlineData("ai-service", "secreto-muy-largo-123", "audit.write", "invalid_scope")]
    public async Task ClientCredentials_rechaza(string clientId, string secret, string? scope, string code)
    {
        var result = await ClientCredentials().ExecuteAsync(new ClientCredentialsCommand(clientId, secret, scope), default);
        Assert.Equal(code, result.Error!.Code);
    }

    [Fact]
    public void SecretsEqual_compara_en_tiempo_constante()
    {
        Assert.True(ClientCredentialsUseCase.SecretsEqual("abc", "abc"));
        Assert.False(ClientCredentialsUseCase.SecretsEqual("abc", "abd"));
        Assert.False(ClientCredentialsUseCase.SecretsEqual("abc", null));
    }

    // ---------------- Consumidor de eventos ----------------

    [Fact]
    public async Task Handler_desactiva_y_reactiva_credencial()
    {
        var user = Seed();
        var handler = new UserStatusChangedHandler(_repo, NullLogger<UserStatusChangedHandler>.Instance);
        Assert.Contains(EventTypes.UserDeactivated, handler.EventTypes);

        await handler.HandleAsync(new IntegrationEvent { EventType = EventTypes.UserDeactivated, Source = "test", SubjectId = user.Id.ToString() }, default);
        Assert.False(user.IsActive);

        await handler.HandleAsync(new IntegrationEvent { EventType = EventTypes.UserActivated, Source = "test", SubjectId = user.Id.ToString() }, default);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task Handler_ignora_eventos_sin_sujeto_o_desconocidos()
    {
        var handler = new UserStatusChangedHandler(_repo, NullLogger<UserStatusChangedHandler>.Instance);
        await handler.HandleAsync(new IntegrationEvent { EventType = EventTypes.UserDeactivated, Source = "t", SubjectId = "no-guid" }, default);
        await handler.HandleAsync(new IntegrationEvent { EventType = EventTypes.UserDeactivated, Source = "t", SubjectId = Guid.NewGuid().ToString() }, default);
        Assert.Equal(0, _repo.SaveCount);
    }
}
