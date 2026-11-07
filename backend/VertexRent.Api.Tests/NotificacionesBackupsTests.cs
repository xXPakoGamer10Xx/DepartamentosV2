using System.Net;
using System.Text.Json.Nodes;

namespace VertexRent.Api.Tests;

/// <summary>Fija el comportamiento de notificaciones, suscripciones push y respaldos (red de seguridad para la reestructura).</summary>
[Collection("api")]
public sealed class NotificacionesBackupsTests(ApiFixture api)
{
    /// <summary>Inquilino vinculado que recibe 2 notificaciones (por dos cuotas extra).</summary>
    async Task<(ApiFixture.Session A, ApiFixture.Session Inq)> ConNotificaciones()
    {
        var a = await api.RegisterAdmin();
        var id = await api.CreateInquilino(a, 1);
        var cuenta = await api.InviteUser(a, "inquilino");
        await a.Put($"/api/inquilinos/{id}/vincular-usuario", new { usuario_id = cuenta.Id });
        await a.Post("/api/cuotas", new { inquilino_id = id, concepto = "Agua", monto = 100 });
        await a.Post("/api/cuotas", new { inquilino_id = id, concepto = "Luz", monto = 50 });
        return (a, cuenta);
    }

    [Fact]
    public async Task Notificaciones_se_listan_marcan_y_borran_solo_las_propias()
    {
        var (a, inq) = await ConNotificaciones();
        var (_, lista) = await inq.Get("/api/notificaciones");
        var notas = lista!["data"]!.AsArray();
        Assert.Equal(2, notas.Count);
        Assert.All(notas, n => Assert.False(n!["leido"]!.GetValue<bool>()));
        var id1 = notas[0]!["id"]!.GetValue<string>(); var id2 = notas[1]!["id"]!.GetValue<string>();

        // una notificación ajena no se toca
        Assert.Equal(HttpStatusCode.NotFound, (await a.Put($"/api/notificaciones/{id1}/leida")).Status);
        var (st, r) = await inq.Put($"/api/notificaciones/{id1}/leida");
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.True(r!["data"]!["leido"]!.GetValue<bool>());

        // borrar las leídas deja solo la no leída
        Assert.Equal(HttpStatusCode.OK, (await inq.Delete("/api/notificaciones/leidas")).Status);
        var restantes = (await inq.Get("/api/notificaciones")).Body!["data"]!.AsArray();
        Assert.Single(restantes); Assert.Equal(id2, restantes[0]!["id"]!.GetValue<string>());

        // borrar una ajena no la elimina (y no falla)
        Assert.Equal(HttpStatusCode.OK, (await a.Delete($"/api/notificaciones/{id2}")).Status);
        Assert.Single((await inq.Get("/api/notificaciones")).Body!["data"]!.AsArray());
        Assert.Equal(HttpStatusCode.OK, (await inq.Put("/api/notificaciones/marcar-leidas")).Status);
        Assert.True((await inq.Get("/api/notificaciones")).Body!["data"]![0]!["leido"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, (await inq.Delete($"/api/notificaciones/{id2}")).Status);
        Assert.Empty((await inq.Get("/api/notificaciones")).Body!["data"]!.AsArray());
    }

    [Fact]
    public async Task Notificaciones_leidas_de_mas_de_7_dias_se_limpian_al_listar()
    {
        var (_, inq) = await ConNotificaciones();
        await inq.Put("/api/notificaciones/marcar-leidas");
        await using (var conn = new Npgsql.NpgsqlConnection(api.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand("UPDATE notificaciones SET created_at = NOW() - interval '8 days' WHERE usuario_id = @u::uuid", conn);
            cmd.Parameters.AddWithValue("u", inq.Id);
            await cmd.ExecuteNonQueryAsync();
        }
        Assert.Empty((await inq.Get("/api/notificaciones")).Body!["data"]!.AsArray());
    }

    [Fact]
    public async Task Notificaciones_exigen_sesion()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.NewClient().GetAsync("/api/notificaciones")).StatusCode);
    }

    [Fact]
    public async Task Suscripcion_push_valida_y_pertenece_a_quien_la_registro_al_final()
    {
        var a = await api.RegisterAdmin(); var b = await api.RegisterAdmin();
        var sub = new { endpoint = $"https://push.test/{Guid.NewGuid()}", keys = new { p256dh = "clave", auth = "auth" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/push/web-subscribe", new { endpoint = "x" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/push/web-subscribe", new { endpoint = "x", keys = new { p256dh = "k" } })).Status);
        Assert.Equal(HttpStatusCode.OK, (await a.Post("/api/push/web-subscribe", sub)).Status);
        Assert.Equal(HttpStatusCode.OK, (await b.Post("/api/push/web-subscribe", sub)).Status); // mismo navegador, otra cuenta
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.NewClient().PostAsync("/api/push/web-subscribe", null)).StatusCode);

        await using var conn = new Npgsql.NpgsqlConnection(api.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("SELECT usuario_id::text FROM web_push_subscriptions WHERE endpoint = @e", conn);
        cmd.Parameters.AddWithValue("e", sub.endpoint);
        Assert.Equal(b.Id, (string?)await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Respaldos_listan_y_no_permiten_salirse_de_la_carpeta()
    {
        var dueno = await api.RegisterAdmin(ApiFixture.PlatformEmail);
        Assert.Equal(HttpStatusCode.OK, (await dueno.Get("/api/backups")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await dueno.Get("/api/backups/no-existe.sql/descargar")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await dueno.Get("/api/backups/..%2F..%2F..%2Fetc%2Fpasswd/descargar")).Status);
        var comun = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.Forbidden, (await comun.Get("/api/backups/no-existe.sql/descargar")).Status);
    }
}
