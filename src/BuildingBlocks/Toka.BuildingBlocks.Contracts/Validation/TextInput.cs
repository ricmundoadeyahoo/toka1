namespace Toka.BuildingBlocks.Contracts.Validation;

/// <summary>
/// Validación/sanitización de texto libre (OWASP Input Validation): se rechaza marcado HTML
/// y caracteres de control para prevenir XSS almacenado y log injection.
/// </summary>
public static class TextInput
{
    public static bool IsSafe(string? value) =>
        value is null || !value.Any(c => c is '<' or '>' || (char.IsControl(c) && c is not '\n' and not '\t'));

    public static string? Normalize(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>Elimina caracteres de control (excepto salto de línea/tab) y recorta a maxLength.</summary>
    public static string Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var cleaned = new string(value.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray()).Trim();
        return cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }

    /// <summary>Valida longitud y seguridad. Devuelve mensaje de error o null.</summary>
    public static string? Validate(string? value, string fieldName, int minLength, int maxLength, bool required = true)
    {
        var normalized = Normalize(value);
        if (normalized is null) return required ? $"{fieldName} es obligatorio." : null;
        if (normalized.Length < minLength) return $"{fieldName} debe tener al menos {minLength} caracteres.";
        if (normalized.Length > maxLength) return $"{fieldName} debe tener como máximo {maxLength} caracteres.";
        if (!IsSafe(normalized)) return $"{fieldName} contiene caracteres no permitidos.";
        return null;
    }
}
