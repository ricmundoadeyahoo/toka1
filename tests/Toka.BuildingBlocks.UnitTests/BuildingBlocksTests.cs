using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Toka.BuildingBlocks.Contracts.Results;
using Toka.BuildingBlocks.Contracts.Security;
using Toka.BuildingBlocks.Contracts.Validation;
using Toka.BuildingBlocks.Infrastructure.Http;
using Toka.BuildingBlocks.Infrastructure.Security;

namespace Toka.BuildingBlocks.UnitTests;

public class ResultTests
{
    [Fact]
    public void Success_y_failure()
    {
        var ok = Result<int>.Success(5);
        Assert.True(ok.IsSuccess);
        Assert.Equal(5, ok.Value);

        Result<int> fail = Error.NotFound("x", "no");
        Assert.True(fail.IsFailure);
        Assert.Equal(ErrorType.NotFound, fail.Error!.Type);
        Assert.Throws<InvalidOperationException>(() => fail.Value);

        Assert.True(Result.Success().IsSuccess);
        Result plain = Error.Conflict("c", "m");
        Assert.True(plain.IsFailure);
    }

    [Theory]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.Unauthorized)]
    [InlineData(ErrorType.Forbidden)]
    [InlineData(ErrorType.Unavailable)]
    [InlineData(ErrorType.RateLimited)]
    public void Fabricas_de_error(ErrorType type)
    {
        var error = type switch
        {
            ErrorType.Validation => Error.Validation("a", "b"),
            ErrorType.Unauthorized => Error.Unauthorized("a", "b"),
            ErrorType.Forbidden => Error.Forbidden("a", "b"),
            ErrorType.Unavailable => Error.Unavailable("a", "b"),
            _ => Error.RateLimited("a", "b")
        };
        Assert.Equal(type, error.Type);
    }

    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.Unavailable, 503)]
    [InlineData(ErrorType.RateLimited, 429)]
    public void Errores_se_mapean_a_status_http(ErrorType type, int status)
    {
        var result = new Error("code", "msg", type).ToProblem();
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public void Exito_devuelve_200_o_204()
    {
        Assert.Equal(200, Assert.IsAssignableFrom<IStatusCodeHttpResult>(Result<int>.Success(1).ToHttpResult()).StatusCode);
        Assert.Equal(204, Assert.IsAssignableFrom<IStatusCodeHttpResult>(Result.Success().ToHttpResult()).StatusCode);
    }
}

public class PagingAndTextTests
{
    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(0, -5, 1, 20)]
    [InlineData(3, 500, 3, 100)]
    [InlineData(2, 10, 2, 10)]
    public void Normaliza_paginacion(int? page, int? size, int expectedPage, int expectedSize) =>
        Assert.Equal((expectedPage, expectedSize), Paging.Normalize(page, size));

    [Fact]
    public void Calcula_total_de_paginas()
    {
        Assert.Equal(3, new PagedResult<int>([], 1, 10, 21).TotalPages);
        Assert.Equal(0, new PagedResult<int>([], 1, 0, 21).TotalPages);
    }

    [Theory]
    [InlineData("Ana López", true)]
    [InlineData("<script>alert(1)</script>", false)]
    [InlineData("línea\u0007", false)]
    [InlineData(null, true)]
    public void IsSafe(string? value, bool expected) => Assert.Equal(expected, TextInput.IsSafe(value));

    [Fact]
    public void Validate_y_sanitize()
    {
        Assert.Null(TextInput.Validate("Ana", "Nombre", 2, 10));
        Assert.Contains("obligatorio", TextInput.Validate("  ", "Nombre", 2, 10));
        Assert.Null(TextInput.Validate(null, "Nombre", 2, 10, required: false));
        Assert.Contains("al menos", TextInput.Validate("A", "Nombre", 2, 10));
        Assert.Contains("como máximo", TextInput.Validate("Anastasia Pérez", "Nombre", 2, 10));
        Assert.Contains("no permitidos", TextInput.Validate("<b>", "Nombre", 2, 10));
        Assert.Equal("hola", TextInput.Sanitize("  ho\u0000la  ", 10));
        Assert.Equal("ab", TextInput.Sanitize("abcdef", 2));
        Assert.Equal(string.Empty, TextInput.Sanitize(null, 5));
        Assert.Null(TextInput.Normalize("   "));
    }
}

public class SecurityHelpersTests
{
    [Theory]
    [InlineData("abc-123_X.y", true)]
    [InlineData("con espacios", false)]
    [InlineData("inyección\nlog", false)]
    [InlineData("", false)]
    public void Valida_correlation_id(string value, bool expected) => Assert.Equal(expected, CorrelationIdMiddleware.IsValid(value));

    [Fact]
    public void Rechaza_correlation_id_demasiado_largo() => Assert.False(CorrelationIdMiddleware.IsValid(new string('a', 65)));

    [Fact]
    public void Extensiones_de_claims()
    {
        var id = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        new[]
        {
            new Claim(TokaClaimTypes.Subject, id.ToString()),
            new Claim(TokaClaimTypes.Email, "a@toka.com"),
            new Claim(TokaClaimTypes.Role, "Admin"),
            new Claim(TokaClaimTypes.Permission, Permissions.UsersRead),
            new Claim(TokaClaimTypes.Scope, "roles.read users.read")
        }, "test"));

        Assert.Equal(id, principal.GetUserId());
        Assert.Equal("a@toka.com", principal.GetEmail());
        Assert.True(principal.HasPermission(Permissions.UsersRead));
        Assert.False(principal.HasPermission(Permissions.UsersWrite));
        Assert.True(principal.HasScope(Scopes.UsersRead));
        Assert.Equal(new[] { "Admin" }, principal.GetRoles());
        Assert.Null(new ClaimsPrincipal().GetUserId());
    }

    [Fact]
    public void Extrae_bearer_token()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer abc.def";
        Assert.Equal("abc.def", http.GetBearerToken());
        http.Request.Headers.Authorization = "Basic xyz";
        Assert.Null(http.GetBearerToken());
    }
}
