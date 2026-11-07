namespace VertexRent.Api.Tests;

[Collection("api")]
public sealed class PlataformaTests
{
    [Fact]
    public void Un_alias_de_gmail_no_hereda_el_privilegio_de_plataforma()
    {
        var antes = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_EMAILS");
        try
        {
            Environment.SetEnvironmentVariable("PLATFORM_ADMIN_EMAILS", "A.B@gmail.com, otro@dominio.test");
            Assert.True(Env.IsPlatformAdmin("a.b@gmail.com"));
            Assert.True(Env.IsPlatformAdmin(" OTRO@dominio.test "));
            Assert.False(Env.IsPlatformAdmin("ab@gmail.com"));
            Assert.False(Env.IsPlatformAdmin("a.b+x@gmail.com"));
            Assert.False(Env.IsPlatformAdmin(null));
        }
        finally { Environment.SetEnvironmentVariable("PLATFORM_ADMIN_EMAILS", antes); }
    }
}
