using System.Net;
using System.Text.Json.Nodes;

namespace VertexRent.Api.Tests;

/// <summary>Fija el comportamiento del módulo de pagos (red de seguridad para la reestructura).</summary>
[Collection("api")]
public sealed class PagosTests(ApiFixture api)
{
    static decimal D(JsonNode? n) => n switch
    {
        null => 0m,
        JsonValue v when v.TryGetValue<string>(out var s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
        JsonValue v => v.GetValue<decimal>(),
        _ => 0m,
    };

    async Task<(ApiFixture.Session A, string Inq)> Nuevo(decimal renta = 5000m)
    {
        var a = await api.RegisterAdmin();
        return (a, await api.CreateInquilino(a, 1, renta));
    }

    static async Task<JsonNode> Estado(ApiFixture.Session a, string inq) => (await a.Get($"/api/pagos/estado/{inq}")).Body!;

    [Fact]
    public async Task Abonos_parciales_descuentan_y_el_ultimo_confirma_el_pago()
    {
        var (a, inq) = await Nuevo();
        var (st, b) = await a.Post("/api/pagos/abono", new { inquilino_id = inq, monto = 1500 });
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.Equal(3500m, D(b!["data"]!["saldoPendiente"]));
        var e = await Estado(a, inq);
        Assert.Equal(1500m, D(e["total_abonado"])); Assert.Equal(3500m, D(e["saldo_pendiente"]));
        Assert.False(e["data"]!["confirmado"]!.GetValue<bool>());

        var (_, b2) = await a.Post("/api/pagos/abono", new { inquilino_id = inq, monto = 3500 });
        Assert.True(b2!["data"]!["pago"]!["confirmado"]!.GetValue<bool>());
        Assert.Equal(0m, D(b2["data"]!["saldoPendiente"]));
        // un periodo ya pagado no admite más abonos
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/pagos/abono", new { inquilino_id = inq, monto = 1 })).Status);
    }

    [Fact]
    public async Task Editar_o_borrar_un_abono_reabre_el_pago()
    {
        var (a, inq) = await Nuevo();
        var (_, b) = await a.Post("/api/pagos/abono", new { inquilino_id = inq, monto = 5000 });
        Assert.True(b!["data"]!["pago"]!["confirmado"]!.GetValue<bool>());
        var abonoId = b["data"]!["abono"]!["id"]!.GetValue<string>();

        var (_, ed) = await a.Put($"/api/pagos/abono/{abonoId}", new { monto = 4000 });
        Assert.False(ed!["data"]!["pago"]!["confirmado"]!.GetValue<bool>());
        Assert.Equal(1000m, D(ed["data"]!["saldoPendiente"]));

        var (_, del) = await a.Delete($"/api/pagos/abono/{abonoId}");
        Assert.Equal(5000m, D(del!["data"]!["saldoPendiente"]));
        // el abono ya no existe
        Assert.Equal(HttpStatusCode.NotFound, (await a.Put($"/api/pagos/abono/{abonoId}", new { monto = 10 })).Status);
    }

    [Fact]
    public async Task Marcar_pagado_completo_cobra_todo_el_saldo()
    {
        var (a, inq) = await Nuevo();
        await a.Post("/api/pagos/abono", new { inquilino_id = inq, monto = 2000 });
        var (st, b) = await a.Post($"/api/pagos/marcar-pagado/{inq}");
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.True(b!["data"]!["confirmado"]!.GetValue<bool>());
        var e = await Estado(a, inq);
        Assert.Equal(5000m, D(e["total_abonado"]));
        // segunda vez: ya estaba pagado
        var (_, again) = await a.Post($"/api/pagos/marcar-pagado/{inq}");
        Assert.True(again!["yaConfirmado"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Las_cuotas_extra_se_cobran_aparte_de_la_renta()
    {
        var (a, inq) = await Nuevo();
        var (sc, _) = await a.Post("/api/cuotas", new { inquilino_id = inq, concepto = "Agua", monto = 300 });
        Assert.Equal(HttpStatusCode.Created, sc);
        var e = await Estado(a, inq);
        Assert.Equal(300m, D(e["total_extra"]));
        await a.Post($"/api/pagos/marcar-pagado/{inq}", new { soloRenta = true });
        var e2 = await Estado(a, inq);
        Assert.True(e2["data"]!["confirmado"]!.GetValue<bool>());
        Assert.Equal(300m, D(e2["total_extra"])); // la cuota sigue pendiente
        Assert.Equal(5000m, D(e2["total_abonado"]));
    }

    [Fact]
    public async Task Cancelar_un_pago_reciente_lo_elimina()
    {
        var (a, inq) = await Nuevo();
        var (_, b) = await a.Post($"/api/pagos/marcar-pagado/{inq}");
        var pagoId = b!["data"]!["id"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.OK, (await a.Delete($"/api/pagos/{pagoId}/cancelar")).Status);
        var e = await Estado(a, inq);
        Assert.Null(e["data"]);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Delete($"/api/pagos/{pagoId}/cancelar")).Status);
    }

    [Fact]
    public async Task Rechazar_un_comprobante_lo_marca_y_uno_nuevo_lo_reabre()
    {
        var (a, inq) = await Nuevo();
        var img = "data:image/jpeg;base64,/9j/4AAQSkZJRg==";
        var (_, c) = await a.Post($"/api/pagos/comprobante/{inq}", new { comprobante_url = img });
        var pagoId = c!["data"]!["id"]!.GetValue<string>();
        var (_, pend) = await a.Get("/api/pagos/comprobantes-pendientes");
        Assert.Single(pend!["data"]!.AsArray());

        var (sr, r) = await a.Post($"/api/pagos/rechazar-admin/{pagoId}", new { comentario = "Borroso" });
        Assert.Equal(HttpStatusCode.OK, sr);
        Assert.True(r!["data"]!["rechazado"]!.GetValue<bool>());
        Assert.Empty((await a.Get("/api/pagos/comprobantes-pendientes")).Body!["data"]!.AsArray());

        var (_, c2) = await a.Post($"/api/pagos/comprobante/{inq}", new { comprobante_url = img });
        Assert.False(c2!["data"]!["rechazado"]!.GetValue<bool>());
        Assert.Single((await a.Get("/api/pagos/comprobantes-pendientes")).Body!["data"]!.AsArray());
    }

    [Fact]
    public async Task Promesa_de_pago_se_guarda_se_lista_y_se_borra()
    {
        var (a, inq) = await Nuevo();
        var (sp, p) = await a.Put($"/api/pagos/promesa/inquilino/{inq}", new { fecha_promesa = "15/12/2030" });
        Assert.Equal(HttpStatusCode.OK, sp);
        Assert.StartsWith("2030-12-15", p!["data"]!["fecha_promesa"]!.GetValue<string>());
        var (_, lista) = await a.Get($"/api/pagos/promesas/{inq}");
        Assert.Single(lista!["data"]!.AsArray());
        Assert.Equal("pendiente", lista["data"]![0]!["estado_cumplimiento"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Put($"/api/pagos/promesa/inquilino/{inq}", new { fecha_promesa = "mañana" })).Status);
        var id = lista["data"]![0]!["id"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.OK, (await a.Delete($"/api/pagos/promesas/{id}")).Status);
    }

    [Fact]
    public async Task Saldo_acumula_los_meses_sin_pagar_desde_el_inicio_del_contrato()
    {
        var (a, inq) = await Nuevo();
        var (_, s) = await a.Get($"/api/pagos/saldo/{inq}");
        Assert.Equal(10000m, D(s!["data"]!["deuda_total"])); // mes anterior + mes en curso
        await a.Post("/api/pagos/abono", new { inquilino_id = inq, monto = 2500 });
        Assert.Equal(7500m, D((await a.Get($"/api/pagos/saldo/{inq}")).Body!["data"]!["deuda_total"]));
        var (_, r) = await a.Get("/api/pagos/saldos/resumen");
        Assert.Equal(7500m, D(r!["data"]!["total_por_cobrar"]));
    }

    // ── Abono aplicado a la deuda más antigua (caso real: abono al mes actual mientras el anterior seguía vencido) ──
    [Fact]
    public async Task Abono_a_deuda_antigua_paga_primero_el_mes_mas_viejo_y_desborda_al_siguiente()
    {
        var (a, inq) = await Nuevo();   // renta 5000: debe el mes anterior y el actual (10 000)
        var (st, b) = await a.Post("/api/pagos/abono", new { inquilino_id = inq, monto = 6000, aplicar_a_deuda_antigua = true });
        Assert.Equal(HttpStatusCode.OK, st);

        var aplicado = b!["data"]!["aplicado"]!.AsArray();
        Assert.Equal(2, aplicado.Count);
        Assert.Equal(5000m, D(aplicado[0]!["monto"]));   // salda el mes más viejo
        Assert.Equal(1000m, D(aplicado[1]!["monto"]));   // y lo que sobra va al siguiente
        Assert.True(string.CompareOrdinal(aplicado[0]!["periodo"]!.GetValue<string>(), aplicado[1]!["periodo"]!.GetValue<string>()) < 0);

        var detalle = (await a.Get($"/api/pagos/saldo/{inq}")).Body!["data"]!["detalle"]!.AsArray();
        Assert.Equal(0m, D(detalle[0]!["saldo"]));
        Assert.Equal(4000m, D(detalle[1]!["saldo"]));
        Assert.Equal(1000m, D((await Estado(a, inq))["total_abonado"]));   // el mes en curso recibió solo el sobrante
    }

    [Fact]
    public async Task Abono_a_deuda_antigua_baja_el_atraso_total()
    {
        var (a, inq) = await Nuevo();
        var antes = D((await a.Get($"/api/pagos/saldo/{inq}")).Body!["data"]!["deuda_total"]);
        await a.Post("/api/pagos/abono", new { inquilino_id = inq, monto = 2600, aplicar_a_deuda_antigua = true });
        Assert.Equal(antes - 2600m, D((await a.Get($"/api/pagos/saldo/{inq}")).Body!["data"]!["deuda_total"]));
        var primero = (await a.Get($"/api/pagos/saldo/{inq}")).Body!["data"]!["detalle"]![0]!;
        Assert.Equal(2400m, D(primero["saldo"]));   // se descontó del mes más viejo, no del actual
    }

    [Fact]
    public async Task Abono_a_deuda_antigua_rechaza_lo_que_excede_el_adeudo_sin_aplicar_nada()
    {
        var (a, inq) = await Nuevo();
        var (st, b) = await a.Post("/api/pagos/abono", new { inquilino_id = inq, monto = 10001, aplicar_a_deuda_antigua = true });
        Assert.Equal(HttpStatusCode.BadRequest, st);
        Assert.Contains("supera el adeudo", b!["message"]!.GetValue<string>());
        Assert.Equal(10000m, D((await a.Get($"/api/pagos/saldo/{inq}")).Body!["data"]!["deuda_total"]));
    }

    [Fact]
    public async Task Abono_con_periodo_explicito_ignora_la_deuda_antigua()
    {
        var (a, inq) = await Nuevo();
        var periodo = DateTime.Now.ToString("yyyy-MM");
        var (st, b) = await a.Post("/api/pagos/abono", new { inquilino_id = inq, monto = 700, periodo, aplicar_a_deuda_antigua = true });
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.Null(b!["data"]!["aplicado"]);
        Assert.Equal(700m, D((await Estado(a, inq))["total_abonado"]));
    }

    [Fact]
    public async Task Abono_a_deuda_antigua_sin_adeudo_da_400()
    {
        var (a, inq) = await Nuevo();
        await a.Post("/api/pagos/abono", new { inquilino_id = inq, monto = 10000, aplicar_a_deuda_antigua = true });
        var (st, _) = await a.Post("/api/pagos/abono", new { inquilino_id = inq, monto = 1, aplicar_a_deuda_antigua = true });
        Assert.Equal(HttpStatusCode.BadRequest, st);
    }
}
