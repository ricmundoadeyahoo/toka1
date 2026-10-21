using Toka.Auth.Domain;

namespace Toka.Auth.UnitTests;

public class EmailAddressTests
{
    [Theory]
    [InlineData("Juan.Perez@Toka.com", "juan.perez@toka.com")]
    [InlineData("  ana@toka.mx ", "ana@toka.mx")]
    public void Create_normaliza_email_valido(string raw, string expected) =>
        Assert.Equal(expected, EmailAddress.Create(raw).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sin-arroba")]
    [InlineData("a@b")]
    [InlineData("Nombre <a@b.com>")]
    public void TryCreate_rechaza_formatos_invalidos(string? raw)
    {
        Assert.False(EmailAddress.TryCreate(raw, out var email));
        Assert.Null(email);
    }

    [Fact]
    public void Create_lanza_DomainException_si_es_invalido()
    {
        var ex = Assert.Throws<DomainException>(() => EmailAddress.Create("x"));
        Assert.Equal("email.invalid", ex.Code);
    }

    [Fact]
    public void Rechaza_email_demasiado_largo() =>
        Assert.False(EmailAddress.TryCreate(new string('a', 250) + "@toka.com", out _));

    [Fact]
    public void ToString_devuelve_valor() => Assert.Equal("a@toka.com", EmailAddress.Create("a@toka.com").ToString());
}

public class PasswordPolicyTests
{
    [Fact]
    public void Contrasena_fuerte_no_tiene_errores() => Assert.Empty(PasswordPolicy.Validate("Segura#2026"));

    [Theory]
    [InlineData("corta1!", "al menos 8")]
    [InlineData("sinmayuscula1!", "mayúscula")]
    [InlineData("SINMINUSCULA1!", "minúscula")]
    [InlineData("SinDigitos!!", "dígito")]
    [InlineData("SinEspecial123", "especial")]
    public void Detecta_cada_regla_incumplida(string password, string expectedFragment) =>
        Assert.Contains(PasswordPolicy.Validate(password), e => e.Contains(expectedFragment));

    [Fact]
    public void Contrasena_vacia_es_obligatoria() =>
        Assert.Single(PasswordPolicy.Validate(""));

    [Fact]
    public void Contrasena_demasiado_larga() =>
        Assert.Contains(PasswordPolicy.Validate("Aa1!" + new string('x', 130)), e => e.Contains("como máximo"));
}

public class UserCredentialTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static UserCredential NewCredential() =>
        UserCredential.Register(Guid.NewGuid(), EmailAddress.Create("u@toka.com"), "hash", Now);

    [Fact]
    public void Register_crea_credencial_activa()
    {
        var c = NewCredential();
        Assert.True(c.IsActive);
        Assert.Equal("u@toka.com", c.Email);
        Assert.Equal(Now, c.CreatedAt);
        Assert.False(c.IsLockedOut(Now));
    }

    [Fact]
    public void Register_valida_id_y_hash()
    {
        Assert.Throws<DomainException>(() => UserCredential.Register(Guid.Empty, EmailAddress.Create("u@toka.com"), "h", Now));
        Assert.Throws<DomainException>(() => UserCredential.Register(Guid.NewGuid(), EmailAddress.Create("u@toka.com"), " ", Now));
    }

    [Fact]
    public void Bloquea_al_alcanzar_maximo_de_intentos()
    {
        var c = NewCredential();
        for (var i = 0; i < 4; i++) Assert.False(c.RegisterFailedLogin(Now, 5, TimeSpan.FromMinutes(15)));
        Assert.True(c.RegisterFailedLogin(Now, 5, TimeSpan.FromMinutes(15)));
        Assert.True(c.IsLockedOut(Now.AddMinutes(14)));
        Assert.False(c.IsLockedOut(Now.AddMinutes(16)));
    }

    [Fact]
    public void Login_exitoso_reinicia_contadores()
    {
        var c = NewCredential();
        c.RegisterFailedLogin(Now, 5, TimeSpan.FromMinutes(15));
        c.RegisterSuccessfulLogin(Now);
        Assert.Equal(0, c.FailedLoginAttempts);
        Assert.Equal(Now, c.LastLoginAt);
        Assert.Null(c.LockoutEnd);
    }

    [Fact]
    public void Configuracion_invalida_de_bloqueo_lanza() =>
        Assert.Throws<DomainException>(() => NewCredential().RegisterFailedLogin(Now, 0, TimeSpan.FromMinutes(1)));

    [Fact]
    public void Desactivar_y_activar()
    {
        var c = NewCredential();
        c.RegisterFailedLogin(Now, 1, TimeSpan.FromMinutes(5));
        c.Deactivate();
        Assert.False(c.IsActive);
        c.Activate();
        Assert.True(c.IsActive);
        Assert.False(c.IsLockedOut(Now));
    }

    [Fact]
    public void Cambiar_hash_valida_valor()
    {
        var c = NewCredential();
        c.ChangePasswordHash("nuevo");
        Assert.Equal("nuevo", c.PasswordHash);
        Assert.Throws<DomainException>(() => c.ChangePasswordHash(""));
    }
}
