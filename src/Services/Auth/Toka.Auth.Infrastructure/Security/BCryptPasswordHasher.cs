using Toka.Auth.Application;

namespace Toka.Auth.Infrastructure.Security;

/// <summary>BCrypt con work factor 11 (OWASP Password Storage Cheat Sheet: ≥10).</summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
