namespace Toka.BuildingBlocks.Contracts.Results;

/// <summary>Tipo de error de negocio. La capa Api lo traduce a un código HTTP.</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    Unavailable,
    RateLimited
}

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);
    public static Error RateLimited(string code, string message) => new(code, message, ErrorType.RateLimited);
}

/// <summary>
/// Result pattern: los casos de uso no lanzan excepciones para flujos de negocio esperados
/// (validación, no encontrado, conflicto). Las excepciones quedan para fallos técnicos.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null) throw new ArgumentException("Un resultado exitoso no puede tener error.");
        if (!isSuccess && error is null) throw new ArgumentException("Un resultado fallido requiere un error.");
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(true, null) => _value = value;
    private Result(Error error) : base(false, error) => _value = default;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("No se puede leer Value de un resultado fallido.");

    public static Result<T> Success(T value) => new(value);
    public static new Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
