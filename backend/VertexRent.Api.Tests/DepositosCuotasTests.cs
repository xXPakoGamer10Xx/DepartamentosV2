using System.Net;
using System.Text.Json.Nodes;

namespace VertexRent.Api.Tests;

/// <summary>Fija el comportamiento de depósitos y cuotas extra (red de seguridad para la reestructura).</summary>
[Collection("api")]
public sealed class DepositosCuotasTests(ApiFixture api)
{
    static decimal D(JsonNode? n) => n switch
    {
        null => 0m,
        JsonValue v when v.TryGetValue<string>(out var s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
        JsonValue v => v.GetValue<decimal>(),
        _ => 0m,
    };

    async Task<(ApiFixture.Session A, string Inq)> ConDeposito(decimal deposito = 2000m)
    {
        var a = await api.RegisterAdmin();
        await a.Post("/api/departamentos", new { numero = 1 });
        var (_, b) = await a.Post("/api/inquilinos", new
        {
            nombre_completo = "Inquilino Depo", depto_numero = 1, renta = 3000, deposito, fecha_pago = "5",
            fecha_inicio = DateTime.Today.ToString("yyyy-MM-dd"), fecha_termino = DateTime.Today.AddMonths(12).ToString("yyyy-MM-dd"),
        });
        return (a, b!["data"]!["id"]!.GetValue<string>());
    }

    /// <summary>Los avisos se envían en segundo plano: se consulta hasta 3 s.</summary>
    static async Task<bool> EsperarNotificacion(ApiFixture.Session s, Func<JsonNode, bool> criterio)
    {
        for (var i = 0; i < 30; i++)
        {
            if ((await s.Get("/api/notificaciones")).Body!["data"]!.AsArray().Any(n => criterio(n!))) return true;
            await Task.Delay(100);
        }
        return false;
    }

    // ── Depósitos ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Deposito_abonos_descuentan_del_saldo()
    {
        var (a, inq) = await ConDeposito();
        var (_, s0) = await a.Get($"/api/depositos/{inq}/saldo");
        Assert.Equal(2000m, D(s0!["data"]!["deposito_total"])); Assert.Equal(0m, D(s0["data"]!["deposito_pagado"])); Assert.Equal(2000m, D(s0["data"]!["deposito_saldo"]));

        var (st, r) = await a.Post("/api/depositos/abono", new { inquilino_id = inq, monto = 750, nota = "primer pago" });
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.Equal(750m, D(r!["data"]!["deposito_pagado"])); Assert.Equal(1250m, D(r["data"]!["deposito_saldo"]));
        Assert.Equal("primer pago", r["data"]!["abono"]!["nota"]!.GetValue<string>());

        await a.Post("/api/depositos/abono", new { inquilino_id = inq, monto = 1250 });
        var (_, s1) = await a.Get($"/api/depositos/{inq}/saldo");
        Assert.Equal(0m, D(s1!["data"]!["deposito_saldo"]));
        Assert.Equal(2, (await a.Get($"/api/depositos/{inq}/abonos")).Body!["data"]!.AsArray().Count);
    }

    [Fact]
    public async Task Deposito_valida_la_entrada_y_el_propietario()
    {
        var (a, inq) = await ConDeposito();
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/depositos/abono", new { monto = 5 })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/depositos/abono", new { inquilino_id = inq, monto = 0 })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/depositos/abono", new { inquilino_id = inq, monto = "abc" })).Status);
        var otro = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Post("/api/depositos/abono", new { inquilino_id = inq, monto = 5 })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await otro.Get($"/api/depositos/{inq}/saldo")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await otro.Get($"/api/depositos/{inq}/abonos")).Status);
    }

    [Fact]
    public async Task Deposito_editar_y_borrar_abonos_es_solo_del_admin()
    {
        var (a, inq) = await ConDeposito();
        var colab = await api.InviteUser(a, "cobrador", new[] { "pagos", "pagos.marcar" });
        var (_, r) = await colab.Post("/api/depositos/abono", new { inquilino_id = inq, monto = 500 }); // el colaborador sí registra
        var id = r!["data"]!["abono"]!["id"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.Forbidden, (await colab.Put($"/api/depositos/abono/{id}", new { monto = 600 })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await colab.Delete($"/api/depositos/abono/{id}")).Status);
        var (st, e) = await a.Put($"/api/depositos/abono/{id}", new { monto = 600 });
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.Equal(600m, D(e!["data"]!["abono"]!["monto"]));
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Put($"/api/depositos/abono/{id}", new { monto = -1 })).Status);
        Assert.Equal(HttpStatusCode.OK, (await a.Delete($"/api/depositos/abono/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Delete($"/api/depositos/abono/{id}")).Status);
        var otro = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Delete($"/api/depositos/abono/{id}")).Status);
    }

    [Fact]
    public async Task Inquilino_ve_su_propio_saldo_de_deposito()
    {
        var (a, inq) = await ConDeposito();
        var cuenta = await api.InviteUser(a, "inquilino");
        await a.Put($"/api/inquilinos/{inq}/vincular-usuario", new { usuario_id = cuenta.Id });
        await a.Post("/api/depositos/abono", new { inquilino_id = inq, monto = 400 });
        Assert.Equal(1600m, D((await cuenta.Get($"/api/depositos/{inq}/saldo")).Body!["data"]!["deposito_saldo"]));
        Assert.True(await EsperarNotificacion(cuenta, n => n["titulo"]!.GetValue<string>().Contains("depósito")), "no llegó el aviso del abono");
    }

    // ── Cuotas extra ─────────────────────────────────────────────────────────
    [Fact]
    public async Task Cuotas_son_solo_para_el_admin()
    {
        var (a, inq) = await ConDeposito();
        var colab = await api.InviteUser(a, "cobrador", new[] { "pagos", "pagos.marcar", "inquilinos" });
        Assert.Equal(HttpStatusCode.Forbidden, (await colab.Get("/api/cuotas")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await colab.Post("/api/cuotas", new { inquilino_id = inq, concepto = "x", monto = 1 })).Status);
    }

    [Fact]
    public async Task Cuota_valida_crea_lista_y_filtra()
    {
        var (a, inq) = await ConDeposito();
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/cuotas", new { inquilino_id = inq, concepto = "  ", monto = 10 })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/cuotas", new { concepto = "Agua", monto = 10 })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/cuotas", new { inquilino_id = inq, concepto = "Agua", monto = -5 })).Status);
        var otro = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.Forbidden, (await otro.Post("/api/cuotas", new { inquilino_id = inq, concepto = "Agua", monto = 10 })).Status);

        var (st, c) = await a.Post("/api/cuotas", new { inquilino_id = inq, concepto = "  Agua  ", monto = 250.5 });
        Assert.Equal(HttpStatusCode.Created, st);
        Assert.Equal("Agua", c!["data"]!["concepto"]!.GetValue<string>());
        Assert.Equal(250.5m, D(c["data"]!["monto"]));
        Assert.Equal("pendiente", c["data"]!["estado"]!.GetValue<string>());

        await a.Post("/api/cuotas", new { inquilino_id = inq, concepto = "Luz", monto = 100 });
        Assert.Equal(2, (await a.Get("/api/cuotas")).Body!["data"]!.AsArray().Count);
        Assert.Equal(2, (await a.Get($"/api/cuotas?inquilino_id={inq}&estado=pendiente")).Body!["data"]!.AsArray().Count);
        Assert.Empty((await a.Get("/api/cuotas?estado=pagado")).Body!["data"]!.AsArray());
        Assert.Empty((await otro.Get("/api/cuotas")).Body!["data"]!.AsArray());
    }

    [Fact]
    public async Task Cuota_se_paga_una_vez_y_solo_se_borra_si_esta_pendiente()
    {
        var (a, inq) = await ConDeposito();
        var (_, c1) = await a.Post("/api/cuotas", new { inquilino_id = inq, concepto = "Agua", monto = 100 });
        var (_, c2) = await a.Post("/api/cuotas", new { inquilino_id = inq, concepto = "Luz", monto = 80 });
        var id1 = c1!["data"]!["id"]!.GetValue<string>(); var id2 = c2!["data"]!["id"]!.GetValue<string>();

        var (st, p) = await a.Put($"/api/cuotas/{id1}/pagar");
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.Equal("pagado", p!["data"]!["estado"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await a.Put($"/api/cuotas/{id1}/pagar")).Status);   // ya pagada
        Assert.Equal(HttpStatusCode.NotFound, (await a.Delete($"/api/cuotas/{id1}")).Status);      // pagada: no se borra
        Assert.Equal(HttpStatusCode.OK, (await a.Delete($"/api/cuotas/{id2}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Delete($"/api/cuotas/{id2}")).Status);
        var otro = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Put($"/api/cuotas/{id1}/pagar")).Status);

        // pagar una cuota no toca la renta del mes
        var (_, e) = await a.Get($"/api/pagos/estado/{inq}");
        Assert.Equal(0m, D(e!["total_abonado"]));
        Assert.Equal(0m, D(e["total_extra"]));
    }

    [Fact]
    public async Task Cuota_avisa_al_inquilino_vinculado()
    {
        var (a, inq) = await ConDeposito();
        var cuenta = await api.InviteUser(a, "inquilino");
        await a.Put($"/api/inquilinos/{inq}/vincular-usuario", new { usuario_id = cuenta.Id });
        await a.Post("/api/cuotas", new { inquilino_id = inq, concepto = "Mantenimiento", monto = 300 });
        var notas = (await cuenta.Get("/api/notificaciones")).Body!["data"]!.AsArray();
        Assert.Contains(notas, n => n!["tipo"]!.GetValue<string>() == "cuota" && n["mensaje"]!.GetValue<string>().Contains("Mantenimiento"));
    }
}
