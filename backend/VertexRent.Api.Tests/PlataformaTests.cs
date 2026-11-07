using System.Net;
using System.Net.Http.Json;

namespace VertexRent.Api.Tests;

/// <summary>Estas pruebas cambian PLATFORM_ADMIN_EMAILS: viven en la colección "api" (se ejecutan en serie).</summary>
[Collection("api")]
public sealed class PlataformaTests(ApiFixture api)
{
    static void ConVariable(string? valor, Action accion)
    {
        var antes = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_EMAILS");
        try { Environment.SetEnvironmentVariable("PLATFORM_ADMIN_EMAILS", valor); accion(); }
        finally { Environment.SetEnvironmentVariable("PLATFORM_ADMIN_EMAILS", antes); }
    }

    [Fact]
    public void Un_alias_de_gmail_no_hereda_el_privilegio_de_plataforma()
    {
        ConVariable("A.B@gmail.com, otro@dominio.test", () =>
        {
            Assert.True(Env.IsPlatformAdmin("a.b@gmail.com"));
            Assert.True(Env.IsPlatformAdmin(" OTRO@dominio.test "));
            Assert.False(Env.IsPlatformAdmin("ab@gmail.com"));
            Assert.False(Env.IsPlatformAdmin("a.b+x@gmail.com"));
            Assert.False(Env.IsPlatformAdmin(null));
            Assert.Contains("a.b@gmail.com", Env.PlatformEmailsNoCanonicos()); // aviso al arrancar
        });
    }

    [Fact]
    public async Task No_se_puede_registrar_un_alias_del_correo_del_dueno()
    {
        var antes = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_EMAILS");
        try
        {
            // El dueño usa una dirección con puntos legada (no canónica): sus alias normalizados no pueden registrarse.
            Environment.SetEnvironmentVariable("PLATFORM_ADMIN_EMAILS", "dueno.legado@gmail.com");
            foreach (var alias in new[] { "duenolegado@gmail.com", "dueno.legado+x@gmail.com", "d.ueno.legado@gmail.com" })
            {
                var res = await api.NewClient().PostAsJsonAsync("/api/auth/register",
                    new { email = alias, password = "Contrasena-123", nombre_completo = "Intruso Prueba", rol = "admin" });
                Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
            }
            // un correo sin relación sigue registrándose con normalidad
            await api.RegisterAdmin();
        }
        finally { Environment.SetEnvironmentVariable("PLATFORM_ADMIN_EMAILS", antes); }
    }
}
