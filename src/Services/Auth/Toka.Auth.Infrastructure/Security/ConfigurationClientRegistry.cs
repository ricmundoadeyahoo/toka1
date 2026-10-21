using Microsoft.Extensions.Configuration;
using Toka.Auth.Application;

namespace Toka.Auth.Infrastructure.Security;

/// <summary>
/// Clientes OAuth2 confidenciales (servicio-a-servicio) leídos de configuración / variables de entorno.
/// Ejemplo: OAuthClients__0__ClientId=ai-service, OAuthClients__0__ClientSecret=..., OAuthClients__0__AllowedScopes__0=users.read
/// </summary>
public sealed class ConfigurationClientRegistry(IConfiguration configuration) : IClientRegistry
{
    private sealed class ClientConfig
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public List<string> AllowedScopes { get; set; } = [];
    }

    private readonly IReadOnlyList<OAuthClient> _clients = (configuration.GetSection("OAuthClients").Get<List<ClientConfig>>() ?? new List<ClientConfig>())
        .Where(c => !string.IsNullOrWhiteSpace(c.ClientId) && c.ClientSecret.Length >= 16)
        .Select(c => new OAuthClient(c.ClientId, c.ClientSecret, c.AllowedScopes))
        .ToList();

    public OAuthClient? Find(string clientId) =>
        _clients.FirstOrDefault(c => string.Equals(c.ClientId, clientId, StringComparison.Ordinal));
}
