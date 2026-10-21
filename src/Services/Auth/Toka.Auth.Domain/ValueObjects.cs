using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Toka.Auth.Domain;

/// <summary>Value Object: email normalizado (minúsculas, sin espacios) y con formato válido.</summary>
public sealed record EmailAddress
{
    public const int MaxLength = 254;

    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? raw, out EmailAddress? email)
    {
        email = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var normalized = raw.Trim().ToLowerInvariant();
        if (normalized.Length > MaxLength) return false;
        if (!MailAddress.TryCreate(normalized, out var parsed) || parsed.Address != normalized) return false;
        if (!normalized.Contains('.', StringComparison.Ordinal)) return false;
        email = new EmailAddress(normalized);
        return true;
    }

    public static EmailAddress Create(string? raw) =>
        TryCreate(raw, out var email) ? email! : throw new DomainException("email.invalid", "El email no tiene un formato válido.");

    public override string ToString() => Value;
}

/// <summary>
/// Política de contraseñas (OWASP ASVS 2.1): longitud mínima 8, máxima 128,
/// mayúscula, minúscula, dígito y carácter especial.
/// </summary>
public static partial class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static IReadOnlyList<string> Validate(string? password)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password))
        {
            errors.Add("La contraseña es obligatoria.");
            return errors;
        }
        if (password.Length < MinLength) errors.Add($"Debe tener al menos {MinLength} caracteres.");
        if (password.Length > MaxLength) errors.Add($"Debe tener como máximo {MaxLength} caracteres.");
        if (!password.Any(char.IsUpper)) errors.Add("Debe incluir una letra mayúscula.");
        if (!password.Any(char.IsLower)) errors.Add("Debe incluir una letra minúscula.");
        if (!password.Any(char.IsDigit)) errors.Add("Debe incluir un dígito.");
        if (!SpecialChar().IsMatch(password)) errors.Add("Debe incluir un carácter especial.");
        return errors;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9]")]
    private static partial Regex SpecialChar();
}
