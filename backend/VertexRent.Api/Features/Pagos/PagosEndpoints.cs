using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Rutas de pagos: enlazan la petición con <see cref="PagosService"/>. Sin SQL ni reglas de negocio.</summary>
public static class PagosEndpoints
{
    public static void Map(WebApplication app)
    {
        var a = app.MapGroup("/api/pagos").Auth();

        // Confirmación por QR: solo personal con permiso de cobro del propietario del pago (nunca anónimo).
        a.MapGet("/info/{token}", H(c => Data(PagosService.InfoPorToken(c.Me(), Api.Route(c, "token"))))).Limit(RateLimits.Token, "token").Permiso("pagos.marcar");
        a.MapPost("/confirmar/{token}", H(async c => Resultado(await PagosService.ConfirmarPorQr(c.Me(), Api.Route(c, "token"))))).Limit(RateLimits.Token, "token").Permiso("pagos.marcar");

        a.MapPost("/generar-qr/{inquilino_id}", H(c => Data(PagosService.GenerarQr(c.Me(), Api.Route(c, "inquilino_id")))));
        a.MapGet("/estados-actuales", H(c => Data(PagosService.EstadosActuales(c.Me())))).Permiso("pagos");
        a.MapGet("/estado/{inquilino_id}", H(async c => Api.Obj(200, (await PagosService.Estado(c.Me(), Api.Route(c, "inquilino_id"))).Select(kv => (kv.Key, kv.Value)).ToArray())))
            .InquilinoOrPermiso("pagos");
        a.MapPost("/comprobante/{inquilino_id}", H(SubirComprobante));
        a.MapGet("/{pago_id}/comprobante", H(async c => Api.Data(await PagosService.Comprobante(c.Me(), Api.Route(c, "pago_id"))))).InquilinoOrPermiso("pagos");
        a.MapPost("/confirmar-admin/{pago_id}", H(async c => Resultado(await PagosService.ConfirmarPorAdmin(c.Me(), Api.Route(c, "pago_id"))))).Permiso("pagos.marcar");
        a.MapPost("/marcar-pagado/{inquilino_id}", H(MarcarPagado)).Permiso("pagos.marcar");
        a.MapDelete("/{pago_id}/cancelar", H(async c => { await PagosService.Cancelar(c.Me(), Api.Route(c, "pago_id")); return Api.Msg("Pago cancelado"); })).Permiso("pagos.marcar");
        a.MapPost("/rechazar-admin/{pago_id}", H(Rechazar)).Permiso("pagos.marcar");
        a.MapGet("/comprobantes-pendientes", H(c => Data(PagosService.ComprobantesPendientes(c.Me())))).Permiso("pagos");
        a.MapGet("/historial/{inquilino_id}", H(c => Data(PagosService.Historial(c.Me(), Api.Route(c, "inquilino_id"))))).InquilinoOrPermiso("pagos");

        // Abonos y saldo/deuda acumulada
        a.MapPost("/abono", H(async c => Api.Data(await PagosService.RegistrarAbono(c.Me(), await Api.Body(c))))).Permiso("pagos.marcar");
        a.MapGet("/abonos/{pago_id}", H(c => Data(PagosService.Abonos(c.Me(), Api.Route(c, "pago_id"))))).InquilinoOrPermiso("pagos");
        a.MapPut("/abono/{abono_id}", H(async c => Api.Data(await PagosService.EditarAbono(c.Me(), Api.Route(c, "abono_id"), await Api.Body(c))))).Permiso("pagos.marcar");
        a.MapDelete("/abono/{abono_id}", H(c => Data(PagosService.EliminarAbono(c.Me(), Api.Route(c, "abono_id"))))).Permiso("pagos.marcar");
        a.MapGet("/saldos/resumen", H(c => Data(PagosService.ResumenDeuda(c.Me())))).Permiso("pagos");
        a.MapGet("/saldos", H(c => Data(PagosService.Saldos(c.Me())))).Permiso("pagos");
        a.MapGet("/saldo/{inquilino_id}", H(c => Data(PagosService.SaldoInquilino(c.Me(), Api.Route(c, "inquilino_id"))))).InquilinoOrPermiso("pagos");
        a.MapPut("/promesa/inquilino/{inquilino_id}", H(async c => Api.Data(await PagosService.SetPromesaInquilino(c.Me(), Api.Route(c, "inquilino_id"), await Api.Body(c))))).Permiso("pagos.marcar");
        a.MapPut("/{pago_id}/promesa", H(async c => Api.Data(await PagosService.SetPromesa(c.Me(), Api.Route(c, "pago_id"), (await Api.Body(c))["fecha_promesa"])))).Permiso("pagos.marcar");
        a.MapGet("/promesas/{inquilino_id}", H(c => Data(PagosService.PromesasHistorial(c.Me(), Api.Route(c, "inquilino_id"))))).InquilinoOrPermiso("pagos");
        a.MapDelete("/promesas/{id}", H(async c => { await PagosService.EliminarPromesa(c.Me(), Api.Route(c, "id")); return Api.Ok(); })).Permiso("pagos.marcar");
    }

    static async Task<IResult> Data<T>(Task<T> trabajo) => Api.Data(await trabajo);

    static IResult Resultado(ResultadoPago r) =>
        r.YaConfirmado ? Api.Obj(200, ("data", r.Pago), ("yaConfirmado", true)) : Api.Data(r.Pago);

    static async Task<IResult> SubirComprobante(HttpContext c)
    {
        var b = await Api.Body(c);
        var imagen = Inputs.Imagen(b["comprobante_url"], "comprobante_url") ?? throw new AppError("comprobante_url es requerido", 400);
        return Api.Data(await PagosService.SubirComprobante(c.Me(), Api.Route(c, "inquilino_id"), imagen));
    }

    static async Task<IResult> MarcarPagado(HttpContext c)
    {
        var b = await Api.Body(c);
        var soloRenta = b["soloRenta"] is JsonValue v && v.TryGetValue<bool>(out var s) && s;
        return Resultado(await PagosService.MarcarPagado(c.Me(), Api.Route(c, "inquilino_id"), soloRenta));
    }

    static async Task<IResult> Rechazar(HttpContext c)
    {
        var b = await Api.Body(c);
        var comentario = b["comentario"] is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : null;
        return Api.Data(await PagosService.Rechazar(c.Me(), Api.Route(c, "pago_id"), comentario));
    }
}
