using System.Net.Mail;

namespace Toka.User.Domain;

public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public enum UserStatus
{
    Active = 1,
    Deactivated = 2
}

/// <summary>
/// Aggregate Root del bounded context "Users": datos de perfil y ciclo de vida del usuario.
/// Las invariantes (longitudes, transiciones de estado) se protegen aquí, no en controladores.
/// </summary>
public sealed class UserProfile
{
    public const int MaxNameLength = 120;
    public const int MaxDepartmentLength = 80;
    public const int MaxJobTitleLength = 80;

    private UserProfile() { } // EF Core

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? Department { get; private set; }
    public string? JobTitle { get; private set; }
    public UserStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => Status == UserStatus.Active;

    public static UserProfile Create(Guid id, string email, string fullName, string? department, DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new DomainException("user.id", "El identificador es obligatorio.");
        if (string.IsNullOrWhiteSpace(email) || !MailAddress.TryCreate(email.Trim(), out _))
            throw new DomainException("user.email", "El email no es válido.");

        var profile = new UserProfile
        {
            Id = id,
            Email = email.Trim().ToLowerInvariant(),
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        profile.SetName(fullName);
        profile.Department = NormalizeOptional(department, MaxDepartmentLength, "user.department");
        return profile;
    }

    public void UpdateProfile(string fullName, string? department, string? jobTitle, DateTimeOffset now)
    {
        SetName(fullName);
        Department = NormalizeOptional(department, MaxDepartmentLength, "user.department");
        JobTitle = NormalizeOptional(jobTitle, MaxJobTitleLength, "user.jobtitle");
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (Status == UserStatus.Deactivated)
            throw new DomainException("user.already_deactivated", "El usuario ya está desactivado.");
        Status = UserStatus.Deactivated;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        if (Status == UserStatus.Active)
            throw new DomainException("user.already_active", "El usuario ya está activo.");
        Status = UserStatus.Active;
        UpdatedAt = now;
    }

    private void SetName(string fullName)
    {
        var name = fullName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length < 2 || name.Length > MaxNameLength)
            throw new DomainException("user.fullname", $"El nombre debe tener entre 2 y {MaxNameLength} caracteres.");
        FullName = name;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string code)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maxLength) throw new DomainException(code, $"El valor excede {maxLength} caracteres.");
        return trimmed;
    }
}
