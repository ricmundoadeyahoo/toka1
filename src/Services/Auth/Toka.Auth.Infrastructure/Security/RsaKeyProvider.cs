using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Toka.Auth.Infrastructure.Security;

public sealed class SigningKeyOptions
{
    public const string SectionName = "SigningKey";

    /// <summary>Ruta del PEM privado. Se persiste en un volumen Docker para sobrevivir reinicios.</summary>
    public string Path { get; set; } = "/keys/signing-key.pem";
}

/// <summary>
/// Gestiona la clave RSA de firma (RS256). Si no existe, la genera (2048 bits) y la persiste.
/// En producción la clave debe venir de un KMS / Key Vault y rotarse (ver Sugerencias).
/// </summary>
public sealed class RsaKeyProvider : IDisposable
{
    private readonly RSA _rsa;

    public RsaKeyProvider(IOptions<SigningKeyOptions> options, ILogger<RsaKeyProvider> logger)
    {
        _rsa = RSA.Create();
        var path = options.Value.Path;
        try
        {
            if (File.Exists(path))
            {
                _rsa.ImportFromPem(File.ReadAllText(path));
                logger.LogInformation("Clave de firma cargada desde {Path}", path);
            }
            else
            {
                _rsa.KeySize = 2048;
                var directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(path, _rsa.ExportRSAPrivateKeyPem());
                logger.LogInformation("Clave de firma generada y guardada en {Path}", path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "No se pudo persistir la clave en {Path}; se usará una clave efímera", path);
        }

        var publicParameters = _rsa.ExportParameters(false);
        KeyId = Base64UrlEncoder.Encode(SHA256.HashData(publicParameters.Modulus!))[..16];
        SigningKey = new RsaSecurityKey(_rsa) { KeyId = KeyId };
        PublicKey = new RsaSecurityKey(publicParameters) { KeyId = KeyId };
    }

    public string KeyId { get; }
    public RsaSecurityKey SigningKey { get; }
    public RsaSecurityKey PublicKey { get; }

    /// <summary>Representación JWKS (RFC 7517) de la clave pública.</summary>
    public object GetJwks()
    {
        var parameters = _rsa.ExportParameters(false);
        return new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = SecurityAlgorithms.RsaSha256,
                    kid = KeyId,
                    n = Base64UrlEncoder.Encode(parameters.Modulus!),
                    e = Base64UrlEncoder.Encode(parameters.Exponent!)
                }
            }
        };
    }

    public void Dispose() => _rsa.Dispose();
}
