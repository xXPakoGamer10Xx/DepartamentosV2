using System.Net;
using System.Text.Json.Nodes;

namespace VertexRent.Api.Tests;

/// <summary>Fija el comportamiento de tickets y departamentos (red de seguridad para la reestructura).</summary>
[Collection("api")]
public sealed class TicketsDepartamentosTests(ApiFixture api)
{
    static async Task<bool> Esperar(ApiFixture.Session s, Func<JsonNode, bool> criterio)
    {
        for (var i = 0; i < 30; i++)
        {
            if ((await s.Get("/api/notificaciones")).Body!["data"]!.AsArray().Any(n => criterio(n!))) return true;
            await Task.Delay(100);
        }
        return false;
    }

    async Task<(ApiFixture.Session A, ApiFixture.Session Inq, string InqId)> Escenario()
    {
        var a = await api.RegisterAdmin();
        var id = await api.CreateInquilino(a, 1);
        var cuenta = await api.InviteUser(a, "inquilino");
        await a.Put($"/api/inquilinos/{id}/vincular-usuario", new { usuario_id = cuenta.Id });
        return (a, cuenta, id);
    }

    // ── Tickets ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task Inquilino_crea_y_ve_solo_sus_tickets_y_avisa_al_admin()
    {
        var (a, inq, _) = await Escenario();
        Assert.Equal(HttpStatusCode.BadRequest, (await inq.Post("/api/tickets", new { titulo = "  ", descripcion = "x" })).Status);
        var (st, t) = await inq.Post("/api/tickets", new { titulo = " Fuga ", descripcion = "En la cocina" });
        Assert.Equal(HttpStatusCode.Created, st);
        Assert.Equal("Fuga", t!["data"]!["titulo"]!.GetValue<string>());
        Assert.Equal("abierto", t["data"]!["estado"]!.GetValue<string>());

        Assert.Single((await inq.Get("/api/tickets")).Body!["data"]!.AsArray());
        var (_, delAdmin) = await a.Get("/api/tickets");
        Assert.Equal("Inquilino Prueba", delAdmin!["data"]![0]!["inquilino_nombre"]!.GetValue<string>());
        Assert.True(await Esperar(a, n => n["tipo"]!.GetValue<string>() == "ticket"), "el admin no recibió el aviso");

        var otro = await api.RegisterAdmin();
        Assert.Empty((await otro.Get("/api/tickets")).Body!["data"]!.AsArray());
    }

    [Fact]
    public async Task Inquilino_sin_departamento_no_puede_crear()
    {
        var a = await api.RegisterAdmin();
        var suelto = await api.InviteUser(a, "inquilino");
        Assert.Equal(HttpStatusCode.Forbidden, (await suelto.Post("/api/tickets", new { titulo = "A", descripcion = "B" })).Status);
        Assert.Empty((await suelto.Get("/api/tickets")).Body!["data"]!.AsArray());
    }

    [Fact]
    public async Task Admin_crea_para_un_inquilino_propio_y_no_para_ajenos()
    {
        var (a, _, id) = await Escenario();
        var otro = await api.RegisterAdmin();
        // Sin inquilino_id el admin recibe 403 (comportamiento actual; el 400 "requerido" no llega a ejecutarse).
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Post("/api/tickets", new { titulo = "A", descripcion = "B" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await otro.Post("/api/tickets", new { titulo = "A", descripcion = "B", inquilino_id = id })).Status);
        Assert.Equal(HttpStatusCode.Created, (await a.Post("/api/tickets", new { titulo = "A", descripcion = "B", inquilino_id = id })).Status);
    }

    [Fact]
    public async Task Atender_un_ticket_cambia_estado_registra_quien_y_avisa_al_inquilino()
    {
        var (a, inq, _) = await Escenario();
        var (_, t) = await inq.Post("/api/tickets", new { titulo = "Luz", descripcion = "No prende" });
        var id = t!["data"]!["id"]!.GetValue<string>();

        var (st, u) = await a.Patch($"/api/tickets/{id}", new { estado = "en_revision", nota_admin = "Voy mañana" });
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.Equal("en_revision", u!["data"]!["estado"]!.GetValue<string>());
        Assert.Equal("Voy mañana", u["data"]!["nota_admin"]!.GetValue<string>());
        Assert.Equal(a.Id, u["data"]!["atendido_por"]!.GetValue<string>());
        Assert.True(await Esperar(inq, n => n["mensaje"]!.GetValue<string>().Contains("revisión")), "el inquilino no recibió el aviso");

        Assert.Equal(HttpStatusCode.Forbidden, (await inq.Patch($"/api/tickets/{id}", new { estado = "resuelto" })).Status);
        var otro = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Patch($"/api/tickets/{id}", new { estado = "resuelto" })).Status);
        // filtros
        Assert.Single((await a.Get("/api/tickets?estado=en_revision")).Body!["data"]!.AsArray());
        Assert.Empty((await a.Get("/api/tickets?estado=resuelto")).Body!["data"]!.AsArray());
        var mes = DateTime.UtcNow.ToString("yyyy-MM");
        Assert.Single((await a.Get($"/api/tickets?mes={mes}")).Body!["data"]!.AsArray());
    }

    [Fact]
    public async Task Borrar_ticket_admin_cualquiera_inquilino_solo_el_suyo_reciente()
    {
        var (a, inq, _) = await Escenario();
        var (_, t) = await inq.Post("/api/tickets", new { titulo = "Uno", descripcion = "x" });
        var (_, t2) = await inq.Post("/api/tickets", new { titulo = "Dos", descripcion = "y" });
        var otro = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Delete($"/api/tickets/{t!["data"]!["id"]!.GetValue<string>()}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await inq.Delete($"/api/tickets/{t["data"]!["id"]!.GetValue<string>()}")).Status);  // dentro de 5 min
        Assert.Equal(HttpStatusCode.OK, (await a.Delete($"/api/tickets/{t2!["data"]!["id"]!.GetValue<string>()}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Delete($"/api/tickets/{t2["data"]!["id"]!.GetValue<string>()}")).Status);
    }

    [Fact]
    public async Task Colaborador_necesita_el_permiso_de_tickets()
    {
        var (a, inq, _) = await Escenario();
        await inq.Post("/api/tickets", new { titulo = "Uno", descripcion = "x" });
        var sin = await api.InviteUser(a, "cobrador", new[] { "pagos" });
        var con = await api.InviteUser(a, "cobrador", new[] { "tickets" });
        Assert.Equal(HttpStatusCode.Forbidden, (await sin.Get("/api/tickets")).Status);
        Assert.Single((await con.Get("/api/tickets")).Body!["data"]!.AsArray());
    }

    // ── Departamentos ────────────────────────────────────────────────────────
    [Fact]
    public async Task Departamento_se_crea_una_vez_por_propietario()
    {
        var a = await api.RegisterAdmin(); var b = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/departamentos", new { descripcion = "sin número" })).Status);
        var (st, d) = await a.Post("/api/departamentos", new { numero = 5, descripcion = "Planta alta", inventario_base = new[] { "Cama", "Mesa" } });
        Assert.Equal(HttpStatusCode.Created, st);
        Assert.Equal("disponible", d!["data"]!["estado"]!.GetValue<string>());
        Assert.Equal(2, d["data"]!["inventario_base"]!.AsArray().Count);
        var (sd, dup) = await a.Post("/api/departamentos", new { numero = 5 });
        Assert.Equal(HttpStatusCode.Conflict, sd);
        Assert.Contains("5", dup!["message"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Created, (await b.Post("/api/departamentos", new { numero = 5 })).Status); // otro propietario
    }

    [Fact]
    public async Task Departamento_lista_detalle_y_actualiza_sin_perder_lo_no_enviado()
    {
        var a = await api.RegisterAdmin();
        await a.Post("/api/departamentos", new { numero = 2, descripcion = "Original", inventario_base = new[] { "Silla" } });
        await api.CreateInquilino(a, 1);

        var (_, lista) = await a.Get("/api/departamentos");
        Assert.Equal(new[] { 1, 2 }, lista!["data"]!.AsArray().Select(x => x!["numero"]!.GetValue<int>()).ToArray());
        Assert.Equal("Inquilino Prueba", lista["data"]![0]!["inquilino_actual"]!["nombre"]!.GetValue<string>());
        Assert.Null(lista["data"]![1]!["inquilino_actual"]);

        var (_, det) = await a.Get("/api/departamentos/1");
        Assert.Single(det!["data"]!["historial_inquilinos"]!.AsArray());
        Assert.Equal(HttpStatusCode.NotFound, (await a.Get("/api/departamentos/77")).Status);

        var (st, u) = await a.Put("/api/departamentos/2", new { estado = "mantenimiento" });
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.Equal("mantenimiento", u!["data"]!["estado"]!.GetValue<string>());
        Assert.Equal("Original", u["data"]!["descripcion"]!.GetValue<string>());
        Assert.Equal("Silla", u["data"]!["inventario_base"]![0]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await a.Put("/api/departamentos/77", new { estado = "ocupado" })).Status);
    }

    [Fact]
    public async Task Departamento_con_inquilino_activo_no_se_elimina()
    {
        var a = await api.RegisterAdmin();
        var inq = await api.CreateInquilino(a, 1);
        await a.Post("/api/departamentos", new { numero = 9 });
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Delete("/api/departamentos/1")).Status);
        Assert.Equal(HttpStatusCode.OK, (await a.Delete("/api/departamentos/9")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Delete("/api/departamentos/9")).Status);
        await a.Delete($"/api/inquilinos/{inq}");
        Assert.Equal(HttpStatusCode.OK, (await a.Delete("/api/departamentos/1")).Status);
    }

    [Fact]
    public async Task Estadisticas_cuentan_estados_ingresos_y_contratos()
    {
        var a = await api.RegisterAdmin();
        await api.CreateInquilino(a, 1, 5000m);
        await a.Post("/api/departamentos", new { numero = 2 });
        await a.Post("/api/departamentos", new { numero = 3, });
        await a.Put("/api/departamentos/3", new { estado = "mantenimiento" });
        var (_, s) = await a.Get("/api/departamentos/stats");
        var d = s!["data"]!;
        Assert.Equal("3", d["total"]!.ToString().Trim('"')); Assert.Equal("1", d["ocupados"]!.ToString().Trim('"'));
        Assert.Equal("1", d["disponibles"]!.ToString().Trim('"')); Assert.Equal("1", d["mantenimiento"]!.ToString().Trim('"'));
        Assert.Equal("5000.00", d["ingresos_mensuales"]!.GetValue<string>());
    }

    [Fact]
    public async Task Departamentos_respeta_permisos_y_propietario()
    {
        var a = await api.RegisterAdmin(); var b = await api.RegisterAdmin();
        await a.Post("/api/departamentos", new { numero = 1 });
        var sin = await api.InviteUser(a, "cobrador", new[] { "pagos" });
        var con = await api.InviteUser(a, "cobrador", new[] { "departamentos" });
        Assert.Equal(HttpStatusCode.Forbidden, (await sin.Get("/api/departamentos")).Status);
        Assert.Equal(HttpStatusCode.OK, (await con.Get("/api/departamentos")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await con.Post("/api/departamentos", new { numero = 4 })).Status); // crear: solo admin
        Assert.Empty((await b.Get("/api/departamentos")).Body!["data"]!.AsArray());
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get("/api/departamentos/1")).Status);
    }
}
