using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace VertexRent.Api.Tests;

/// <summary>Aislamiento entre propietarios, roles y sesiones. Cada prueba falla sin la corrección correspondiente.</summary>
[Collection("api")]
public sealed class SeguridadTests(ApiFixture api)
{
    // Dos propietarios independientes, cada uno con un inquilino; y un colaborador del propietario A.
    async Task<(ApiFixture.Session A, ApiFixture.Session B, ApiFixture.Session ColabA, string InqA, string InqB)> Escenario(string[]? permisosColab = null)
    {
        var a = await api.RegisterAdmin();
        var b = await api.RegisterAdmin();
        var colab = await api.InviteUser(a, "cobrador", permisosColab ?? new[] { "pagos", "pagos.marcar", "inquilinos" });
        var inqA = await api.CreateInquilino(a, 1);
        var inqB = await api.CreateInquilino(b, 1);
        return (a, b, colab, inqA, inqB);
    }

    static string Str(JsonNode? n, params string[] path)
    {
        foreach (var p in path) n = n?[p];
        return n!.GetValue<string>();
    }

    // ── Aislamiento entre propietarios ───────────────────────────────────────
    [Fact]
    public async Task Colaborador_no_ve_historial_ni_saldo_de_otro_propietario()
    {
        var e = await Escenario();
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ColabA.Get($"/api/pagos/historial/{e.InqB}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ColabA.Get($"/api/pagos/saldo/{e.InqB}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ColabA.Get($"/api/depositos/{e.InqB}/saldo")).Status);
        // y sí ve lo de su propietario
        Assert.Equal(HttpStatusCode.OK, (await e.ColabA.Get($"/api/pagos/historial/{e.InqA}")).Status);
    }

    [Fact]
    public async Task Colaborador_no_registra_abonos_en_inquilino_de_otro_propietario()
    {
        var e = await Escenario();
        var (st, _) = await e.ColabA.Post("/api/pagos/abono", new { inquilino_id = e.InqB, monto = 100 });
        Assert.Equal(HttpStatusCode.NotFound, st);
        var (st2, _) = await e.ColabA.Post("/api/depositos/abono", new { inquilino_id = e.InqB, monto = 100 });
        Assert.Equal(HttpStatusCode.NotFound, st2);
    }

    [Fact]
    public async Task Listados_de_cobranza_solo_incluyen_inquilinos_propios()
    {
        var e = await Escenario();
        var (_, saldos) = await e.ColabA.Get("/api/pagos/saldos");
        var ids = saldos!["data"]!.AsArray().Select(x => x!["inquilino_id"]!.GetValue<string>()).ToList();
        Assert.Contains(e.InqA, ids);
        Assert.DoesNotContain(e.InqB, ids);

        var (_, estados) = await e.ColabA.Get("/api/pagos/estados-actuales");
        var ids2 = estados!["data"]!.AsArray().Select(x => x!["inquilino_id"]!.GetValue<string>()).ToList();
        Assert.DoesNotContain(e.InqB, ids2);
    }

    [Fact]
    public async Task Admin_no_cambia_el_rol_de_usuarios_ajenos()
    {
        var e = await Escenario();
        // el colaborador pertenece a A: B no puede tocarlo
        var (st, _) = await e.B.Patch($"/api/usuarios/{e.ColabA.Id}/rol", new { rol = "inquilino" });
        Assert.Equal(HttpStatusCode.Forbidden, st);
        // y nadie puede crear admins por esta vía
        var (st2, _) = await e.A.Patch($"/api/usuarios/{e.ColabA.Id}/rol", new { rol = "admin" });
        Assert.Equal(HttpStatusCode.BadRequest, st2);
        // sobre un usuario propio sí funciona
        Assert.Equal(HttpStatusCode.OK, (await e.A.Patch($"/api/usuarios/{e.ColabA.Id}/rol", new { rol = "cobrador" })).Status);
    }

    [Fact]
    public async Task Vincular_usuario_exige_una_cuenta_propia()
    {
        var e = await Escenario();
        var inqDeB = await api.InviteUser(e.B, "inquilino");
        var (st, _) = await e.A.Put($"/api/inquilinos/{e.InqA}/vincular-usuario", new { usuario_id = inqDeB.Id });
        Assert.Equal(HttpStatusCode.NotFound, st);
    }

    [Fact]
    public async Task Cuenta_bancaria_no_mezcla_la_renta_de_otros_propietarios()
    {
        var e = await Escenario();
        var (_, cuentaA) = await e.A.Post("/api/cuentas-bancarias", new { banco_clabe = "012345678901234567" });
        var cuentaId = Str(cuentaA, "data", "id");
        await e.A.Patch("/api/cuentas-bancarias/departamento/1", new { cuenta_bancaria_id = cuentaId });
        // El depto 1 de B (mismo número) no debe sumar a la cuenta de A.
        var (_, lista) = await e.A.Get("/api/cuentas-bancarias");
        var renta = lista!["data"]!.AsArray().First(c => Str(c, "id") == cuentaId)!["renta_mensual"]!.GetValue<string>();
        Assert.Equal(5000m, decimal.Parse(renta, System.Globalization.CultureInfo.InvariantCulture));
    }

    // ── Respaldos solo para el dueño de la plataforma ────────────────────────
    [Fact]
    public async Task Respaldos_solo_para_el_dueno_de_la_plataforma()
    {
        var comun = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.Forbidden, (await comun.Get("/api/backups")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await comun.Post("/api/backups/manual")).Status);

        var dueno = await api.RegisterAdmin(ApiFixture.PlatformEmail);
        Assert.Equal(HttpStatusCode.OK, (await dueno.Get("/api/backups")).Status);
        var (_, me) = await dueno.Get("/api/auth/me");
        Assert.True(me!["data"]!["plataforma"]!.GetValue<bool>());
        var (_, meComun) = await comun.Get("/api/auth/me");
        Assert.False(meComun!["data"]!["plataforma"]!.GetValue<bool>());
    }

    // ── Confirmación por QR ──────────────────────────────────────────────────
    async Task<(ApiFixture.Session A, ApiFixture.Session Inquilino, string InqId, string Token, string PagoId)> ConQr()
    {
        var a = await api.RegisterAdmin();
        var inqId = await api.CreateInquilino(a, 1);
        var inquilino = await api.InviteUser(a, "inquilino");
        Assert.Equal(HttpStatusCode.OK, (await a.Put($"/api/inquilinos/{inqId}/vincular-usuario", new { usuario_id = inquilino.Id })).Status);
        var (st, qr) = await inquilino.Post($"/api/pagos/generar-qr/{inqId}");
        Assert.Equal(HttpStatusCode.OK, st);
        return (a, inquilino, inqId, Str(qr, "data", "qr_token"), Str(qr, "data", "id"));
    }

    [Fact]
    public async Task Confirmar_por_qr_exige_sesion_y_permiso()
    {
        var q = await ConQr();
        var anonimo = api.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync($"/api/pagos/confirmar/{q.Token}", new { scanner_id = q.Inquilino.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/pagos/info/{q.Token}")).StatusCode);
        // el propio inquilino no puede marcar su renta como pagada
        Assert.Equal(HttpStatusCode.Forbidden, (await q.Inquilino.Post($"/api/pagos/confirmar/{q.Token}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await q.Inquilino.Get($"/api/pagos/info/{q.Token}")).Status);

        var (st, body) = await q.A.Post($"/api/pagos/confirmar/{q.Token}");
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.True(body!["data"]!["confirmado"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Confirmar_por_qr_no_funciona_para_otro_propietario()
    {
        var q = await ConQr();
        var otro = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Post($"/api/pagos/confirmar/{q.Token}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Get($"/api/pagos/info/{q.Token}")).Status);
    }

    [Fact]
    public async Task Info_del_qr_no_expone_el_comprobante()
    {
        var q = await ConQr();
        var (_, info) = await q.A.Get($"/api/pagos/info/{q.Token}");
        Assert.Null(info!["data"]!["comprobante_url"]);
        Assert.Null(info["data"]!["qr_token"]);
    }

    [Fact]
    public async Task Dos_confirmaciones_simultaneas_no_duplican_el_abono()
    {
        var q = await ConQr();
        var tareas = Enumerable.Range(0, 6).Select(_ => q.A.Post($"/api/pagos/confirmar/{q.Token}"));
        await Task.WhenAll(tareas);
        var (_, abonos) = await q.A.Get($"/api/pagos/abonos/{q.PagoId}");
        var total = abonos!["data"]!.AsArray().Sum(x => decimal.Parse(x!["monto"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(5000m, total);
    }

    // ── Roles y permisos ─────────────────────────────────────────────────────
    [Fact]
    public async Task Colaborador_de_solo_lectura_no_registra_depositos_ni_abonos()
    {
        var e = await Escenario(new[] { "pagos", "reportes", "cuentas", "inquilinos", "departamentos" }); // preset "Contador"
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ColabA.Post("/api/depositos/abono", new { inquilino_id = e.InqA, monto = 10 })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ColabA.Post("/api/pagos/abono", new { inquilino_id = e.InqA, monto = 10 })).Status);
        Assert.Equal(HttpStatusCode.OK, (await e.ColabA.Get("/api/pagos/saldos")).Status);
    }

    [Fact]
    public async Task Colaborador_sin_permiso_pagos_no_ve_la_cobranza()
    {
        var e = await Escenario(new[] { "tickets" });
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ColabA.Get("/api/pagos/saldos")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ColabA.Get("/api/pagos/saldos/resumen")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.ColabA.Get($"/api/pagos/historial/{e.InqA}")).Status);
    }

    [Fact]
    public async Task Inquilino_no_ve_codigos_ni_rfc_en_la_configuracion()
    {
        var a = await api.RegisterAdmin();
        var inqId = await api.CreateInquilino(a, 1);
        var inquilino = await api.InviteUser(a, "inquilino");
        await a.Put($"/api/inquilinos/{inqId}/vincular-usuario", new { usuario_id = inquilino.Id });
        await a.Put("/api/config", new { arrendador_nombre = "Don Arrendador", arrendador_rfc = "XAXX010101000", admin_invite_code = "SECRETO" });

        var (_, cfg) = await inquilino.Get("/api/config");
        Assert.Equal("Don Arrendador", Str(cfg, "data", "arrendador_nombre"));
        Assert.Null(cfg!["data"]!["admin_invite_code"]);
        Assert.Null(cfg["data"]!["arrendador_rfc"]);
    }

    // ── Sesiones ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Usuario_desactivado_pierde_la_sesion_de_inmediato()
    {
        var a = await api.RegisterAdmin();
        var colab = await api.InviteUser(a, "cobrador", new[] { "pagos" });
        Assert.Equal(HttpStatusCode.OK, (await colab.Get("/api/auth/me")).Status);
        Assert.Equal(HttpStatusCode.OK, (await a.Put($"/api/usuarios/{colab.Id}/toggle")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await colab.Get("/api/auth/me")).Status);
    }

    [Fact]
    public async Task Cambiar_la_contrasena_cierra_las_demas_sesiones()
    {
        var email = api.UniqueEmail("cambio");
        var primera = await api.Register(email, "admin");
        var segunda = await api.Login(email);

        var (st, body) = await primera.Post("/api/auth/change-password", new { passwordActual = "Contrasena-123", passwordNueva = "Otra-contrasena-456" });
        Assert.Equal(HttpStatusCode.OK, st);

        Assert.Equal(HttpStatusCode.Unauthorized, (await segunda.Get("/api/auth/me")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await primera.Get("/api/auth/me")).Status); // token viejo
        primera.SetToken(Str(body, "data", "token")); // el nuevo sigue valiendo
        Assert.Equal(HttpStatusCode.OK, (await primera.Get("/api/auth/me")).Status);
        await api.Login(email, "Otra-contrasena-456");
    }

    [Fact]
    public async Task Colaborador_cambia_su_propia_contrasena_y_no_la_del_admin()
    {
        var a = await api.RegisterAdmin();
        var colab = await api.InviteUser(a, "cobrador", new[] { "pagos" });
        var (st, _) = await colab.Post("/api/auth/change-password", new { passwordActual = "Contrasena-123", passwordNueva = "Nueva-contrasena-789" });
        Assert.Equal(HttpStatusCode.OK, st);
        await api.Login(a.Email); // la contraseña del admin sigue igual
        await api.Login(colab.Email, "Nueva-contrasena-789");
    }

    [Fact]
    public async Task Token_de_pdf_no_sirve_como_sesion()
    {
        var a = await api.RegisterAdmin();
        var inqId = await api.CreateInquilino(a, 1);
        var (_, t) = await a.Post($"/api/inquilinos/{inqId}/pdf-token");
        var scoped = api.NewClient();
        scoped.DefaultRequestHeaders.Authorization = new("Bearer", Str(t, "data", "token"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await scoped.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Sse_solo_acepta_boletos_de_corta_duracion_y_no_la_sesion()
    {
        var a = await api.RegisterAdmin();
        var anonimo = api.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/events?token={a.Token}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/events?ticket={a.Token}")).StatusCode);
        var (st, t) = await a.Post("/api/events/ticket");
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.False(string.IsNullOrEmpty(Str(t, "data", "ticket")));
    }

    // ── Datos de entrada y errores ───────────────────────────────────────────
    [Fact]
    public async Task Identificador_invalido_da_400_sin_filtrar_detalles_internos()
    {
        var a = await api.RegisterAdmin();
        var (st, body) = await a.Get("/api/inquilinos/no-es-un-uuid");
        Assert.Equal(HttpStatusCode.BadRequest, st);
        var msg = Str(body, "message");
        Assert.DoesNotContain("uuid", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invalid input syntax", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Comprobante_solo_acepta_imagenes()
    {
        var a = await api.RegisterAdmin();
        var inqId = await api.CreateInquilino(a, 1);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post($"/api/pagos/comprobante/{inqId}", new { comprobante_url = "javascript:alert(1)" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post($"/api/pagos/comprobante/{inqId}", new { comprobante_url = "data:text/html;base64,PHNjcmlwdD4=" })).Status);
        var ok = await a.Post($"/api/pagos/comprobante/{inqId}", new { comprobante_url = "data:image/jpeg;base64,/9j/4AAQSkZJRg==" });
        Assert.Equal(HttpStatusCode.OK, ok.Status);
    }

    [Fact]
    public async Task Abono_valida_periodo_y_fecha()
    {
        var a = await api.RegisterAdmin();
        var inqId = await api.CreateInquilino(a, 1);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/pagos/abono", new { inquilino_id = inqId, monto = 10, periodo = "2026-13" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/pagos/abono", new { inquilino_id = inqId, monto = 10, fecha = "ayer" })).Status);
        Assert.Equal(HttpStatusCode.OK, (await a.Post("/api/pagos/abono", new { inquilino_id = inqId, monto = 10 })).Status);
    }

    [Fact]
    public async Task Codigo_de_invitacion_solo_se_usa_una_vez_aunque_se_registren_en_paralelo()
    {
        var a = await api.RegisterAdmin();
        var (_, c) = await a.Post("/api/invite-codes", new { rol = "inquilino" });
        var code = Str(c, "data", "codigo");
        var intentos = Enumerable.Range(0, 5).Select(async _ =>
        {
            var http = api.NewClient();
            var res = await http.PostAsJsonAsync("/api/auth/register",
                new { email = api.UniqueEmail("par"), password = "Contrasena-123", nombre_completo = "Par Alelo", rol = "inquilino", invite_code = code });
            return res.StatusCode;
        });
        var resultados = await Task.WhenAll(intentos);
        Assert.Equal(1, resultados.Count(r => r == HttpStatusCode.Created));
    }

    [Fact]
    public async Task Correo_con_formato_gmail_puede_iniciar_sesion_tras_crearlo_el_admin()
    {
        var a = await api.RegisterAdmin();
        var (st, _) = await a.Post("/api/usuarios", new { email = "Mi.Colab+x@gmail.com", password = "Contrasena-123", nombre_completo = "Colab Gmail", rol = "cobrador" });
        Assert.Equal(HttpStatusCode.Created, st);
        await api.Login("Mi.Colab+x@gmail.com");
    }
}
