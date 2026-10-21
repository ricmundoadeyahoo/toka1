using Toka.Auth.Application;
using Toka.Auth.Domain;

namespace Toka.Auth.UnitTests;

internal sealed class InMemoryCredentialRepository : ICredentialRepository
{
    public List<UserCredential> Items { get; } = [];
    public int SaveCount { get; private set; }

    public Task<UserCredential?> GetByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(c => c.Email == normalizedEmail));

    public Task<UserCredential?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(c => c.Id == id));

    public Task<bool> ExistsByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        Task.FromResult(Items.Any(c => c.Email == normalizedEmail));

    public Task AddAsync(UserCredential credential, CancellationToken ct)
    {
        Items.Add(credential);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

/// <summary>Hasher trivial y rápido para pruebas (BCrypt real se prueba aparte).</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public int HashCalls { get; private set; }
    public string Hash(string password)
    {
        HashCalls++;
        return "hashed:" + password;
    }
    public bool Verify(string password, string hash) => hash == "hashed:" + password;
}

internal sealed class FakeTokenService : ITokenService
{
    public AccessTokenRequest? LastRequest { get; private set; }
    private int _counter;

    public IssuedAccessToken CreateAccessToken(AccessTokenRequest request)
    {
        LastRequest = request;
        return new IssuedAccessToken($"access-{++_counter}", $"jti-{_counter}", new DateTimeOffset(2026, 1, 15, 12, 15, 0, TimeSpan.Zero));
    }

    public string CreateRefreshToken() => $"refresh-{++_counter}";
}

internal sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    public Dictionary<string, RefreshTokenEntry> Tokens { get; } = new();

    public Task StoreAsync(string refreshToken, RefreshTokenEntry entry, CancellationToken ct)
    {
        Tokens[refreshToken] = entry;
        return Task.CompletedTask;
    }

    public Task<RefreshTokenEntry?> ConsumeAsync(string refreshToken, CancellationToken ct)
    {
        Tokens.Remove(refreshToken, out var entry);
        return Task.FromResult(entry);
    }

    public Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        Tokens.Remove(refreshToken);
        return Task.CompletedTask;
    }
}

internal sealed class FakeRoleServiceClient : IRoleServiceClient
{
    public UserAuthorization? Response { get; set; } = new(["User"], ["ai:use"]);
    public Task<UserAuthorization?> GetUserAuthorizationAsync(Guid userId, CancellationToken ct) => Task.FromResult(Response);
}
