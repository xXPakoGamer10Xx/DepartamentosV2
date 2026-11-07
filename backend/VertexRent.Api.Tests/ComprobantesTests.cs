using System.Net;
using System.Text.Json.Nodes;

namespace VertexRent.Api.Tests;

/// <summary>Los listados no arrastran la imagen del comprobante (base64): solo indican si existe y se pide aparte.</summary>
[Collection("api")]
public sealed class ComprobantesTests(ApiFixture api)
{
    const string Imagen = "data:image/jpeg;base64,/9j/4AAQSkZJRg==";

    async Task<(ApiFixture.Session A, ApiFixture.Session Inq, string InqId, string PagoId)> Escenario(bool conComprobante = true)
    {
        var a = await api.RegisterAdmin();
        var id = await api.CreateInquilino(a, 1);
        var cuenta = await api.InviteUser(a, "inquilino");
        await a.Put($"/api/inquilinos/{id}/vincular-usuario", new { usuario_id = cuenta.Id });
        string pagoId;
        if (conComprobante)
        {
            var (_, p) = await cuenta.Post($"/api/pagos/comprobante/{id}", new { comprobante_url = Imagen });
            pagoId = p!["data"]!["id"]!.GetValue<string>();
        }
        else pagoId = (await a.Post($"/api/pagos/abono", new { inquilino_id = id, monto = 100 })).Body!["data"]!["pago"]!["id"]!.GetValue<string>();
        return (a, cuenta, id, pagoId);
    }

    [Fact]
    public async Task Estados_actuales_indican_si_hay_comprobante_sin_enviarlo()
    {
        var (a, _, id, _) = await Escenario();
        var fila = (await a.Get("/api/pagos/estados-actuales")).Body!["data"]!.AsArray().First(x => x!["inquilino_id"]!.GetValue<string>() == id)!;
        Assert.True(fila["tiene_comprobante"]!.GetValue<bool>());
        Assert.Null(fila["comprobante_url"]);
    }

    [Fact]
    public async Task Estados_actuales_sin_comprobante_dicen_falso()
    {
        var (a, _, id, _) = await Escenario(conComprobante: false);
        var fila = (await a.Get("/api/pagos/estados-actuales")).Body!["data"]!.AsArray().First(x => x!["inquilino_id"]!.GetValue<string>() == id)!;
        Assert.False(fila["tiene_comprobante"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Historial_no_incluye_la_imagen_pero_si_el_indicador()
    {
        var (a, inq, id, _) = await Escenario();
        foreach (var s in new[] { a, inq })
        {
            var fila = (await s.Get($"/api/pagos/historial/{id}")).Body!["data"]![0]!;
            Assert.True(fila["tiene_comprobante"]!.GetValue<bool>());
            Assert.Null(fila["comprobante_url"]);
            Assert.NotNull(fila["periodo"]); Assert.NotNull(fila["monto"]); Assert.NotNull(fila["a_tiempo"] ?? JsonValue.Create(0));   // el resto del contrato sigue igual
        }
    }

    [Fact]
    public async Task Abonos_no_incluyen_la_imagen_pero_si_el_indicador()
    {
        var (a, _, id, _) = await Escenario(conComprobante: false);
        await a.Post("/api/pagos/abono", new { inquilino_id = id, monto = 50, comprobante_url = Imagen });
        var pagoId = (await a.Get($"/api/pagos/estado/{id}")).Body!["data"]!["id"]!.GetValue<string>();
        var abono = (await a.Get($"/api/pagos/abonos/{pagoId}")).Body!["data"]!.AsArray().Last()!;
        Assert.True(abono["tiene_comprobante"]!.GetValue<bool>());
        Assert.Null(abono["comprobante_url"]);

        await a.Post("/api/depositos/abono", new { inquilino_id = id, monto = 10, comprobante_url = Imagen });
        var dep = (await a.Get($"/api/depositos/{id}/abonos")).Body!["data"]![0]!;
        Assert.True(dep["tiene_comprobante"]!.GetValue<bool>()); Assert.Null(dep["comprobante_url"]);
    }

    [Fact]
    public async Task La_imagen_se_pide_aparte_con_permisos()
    {
        var (a, inq, _, pagoId) = await Escenario();
        var otro = await api.RegisterAdmin();
        var sin = await api.InviteUser(a, "cobrador", new[] { "tickets" });
        var con = await api.InviteUser(a, "cobrador", new[] { "pagos" });

        foreach (var s in new[] { a, inq, con })
        {
            var (st, b) = await s.Get($"/api/pagos/{pagoId}/comprobante");
            Assert.Equal(HttpStatusCode.OK, st);
            Assert.Equal(Imagen, b!["data"]!["comprobante_url"]!.GetValue<string>());
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await otro.Get($"/api/pagos/{pagoId}/comprobante")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await sin.Get($"/api/pagos/{pagoId}/comprobante")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.NewClient().GetAsync($"/api/pagos/{pagoId}/comprobante")).StatusCode);
    }

    [Fact]
    public async Task Pago_sin_comprobante_da_404_en_la_peticion_aparte()
    {
        var (a, _, _, pagoId) = await Escenario(conComprobante: false);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Get($"/api/pagos/{pagoId}/comprobante")).Status);
    }
}
