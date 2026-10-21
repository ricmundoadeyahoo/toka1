namespace Toka.Auth.Domain;

/// <summary>
/// Aggregate Root del bounded context Identity. Solo conoce lo necesario para autenticar:
/// email, hash de contraseña y estado de bloqueo. El perfil (nombre, departamento...) vive en User Service.
/// </summary>
public sealed class UserCredential
{
    private UserCredential() { } // EF Core

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockoutEnd { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    public static UserCredential Register(Guid id, EmailAddress email, string passwordHash, DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new DomainException("credential.id", "El identificador es obligatorio.");
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new DomainException("credential.hash", "El hash de contraseña es obligatorio.");

        return new UserCredential
        {
            Id = id,
            Email = email.Value,
            PasswordHash = passwordHash,
            IsActive = true,
            CreatedAt = now
        };
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is not null && LockoutEnd > now;

    /// <summary>Registra un intento fallido. Devuelve true si este intento provocó el bloqueo.</summary>
    public bool RegisterFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        if (maxAttempts <= 0) throw new DomainException("lockout.config", "maxAttempts debe ser mayor a cero.");
        FailedLoginAttempts++;
        if (FailedLoginAttempts < maxAttempts) return false;

        LockoutEnd = now.Add(lockoutDuration);
        FailedLoginAttempts = 0;
        return true;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        LastLoginAt = now;
    }

    public void ChangePasswordHash(string newHash)
    {
        if (string.IsNullOrWhiteSpace(newHash)) throw new DomainException("credential.hash", "El hash de contraseña es obligatorio.");
        PasswordHash = newHash;
    }

    public void Deactivate() => IsActive = false;

    public void Activate()
    {
        IsActive = true;
        FailedLoginAttempts = 0;
        LockoutEnd = null;
    }
}
