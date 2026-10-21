namespace Toka.Auth.Domain;

/// <summary>Violación de una invariante del dominio Identity.</summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
