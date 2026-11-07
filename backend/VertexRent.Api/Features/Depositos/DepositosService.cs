using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Reglas del módulo de depósitos. No conoce HTTP; el SQL vive en <see cref="DepositosRepository"/>.</summary>
public static class DepositosService
{
    public static async Task<Dictionary<string, object?>> Saldo(AuthUser u, string inquilinoId)
    {
        var inq = await InquilinosRepository.Accesible(inquilinoId, u, "id, deposito") ?? throw new AppError("No autorizado", 403);
        var pagado = await DepositosRepository.TotalPagado(inquilinoId);
        var total = inq.D("deposito");
        return new()
        {
            ["deposito_total"] = (double)total, ["deposito_pagado"] = (double)pagado,
            ["deposito_saldo"] = (double)Math.Max(total - pagado, 0),
        };
    }

    public static async Task<List<Row>> Abonos(AuthUser u, string inquilinoId)
    {
        if (await InquilinosRepository.Accesible(inquilinoId, u) == null) throw new AppError("No autorizado", 403);
        return await DepositosRepository.Abonos(inquilinoId);
    }

    public static async Task<Dictionary<string, object?>> RegistrarAbono(AuthUser u, JsonObject b)
    {
        if (!Js.T(b["inquilino_id"])) throw new AppError("inquilino_id es requerido", 400);
        var monto = Js.ParseFloat(b["monto"]);
        if (double.IsNaN(monto) || monto <= 0) throw new AppError("El monto del abono debe ser mayor a 0", 400);

        var inquilinoId = b.S("inquilino_id")!;
        var inquilino = await InquilinosRepository.ActivoDelPropietario(inquilinoId, u.Id)
            ?? throw new AppError("Inquilino no encontrado o no autorizado", 404);

        var metodo = Js.T(b["metodo"]) ? b.S("metodo") : (string.IsNullOrEmpty(inquilino.S("metodo_pago")) ? "efectivo" : inquilino.S("metodo_pago"));
        var abono = await DepositosRepository.Insertar(inquilinoId, (decimal)monto, Inputs.Fecha(b["fecha"], "fecha"), metodo,
            Js.T(b["nota"]) ? b.S("nota") : null, Inputs.Imagen(b["comprobante_url"], "comprobante_url"), u.Actor);

        var pagado = await DepositosRepository.TotalPagado(inquilinoId);
        var saldo = Math.Max(inquilino.D("deposito") - pagado, 0);

        var usuarioId = inquilino.S("usuario_id");
        if (!string.IsNullOrEmpty(usuarioId))
            Js.Bg(() => PushService.CreateAndSendNotification(usuarioId, "💰 Abono a depósito registrado",
                $"Se registró un abono de ${Js.NumMx(monto)} a tu depósito. Saldo pendiente: ${Js.NumMx((double)saldo)}", "pago"));

        return new() { ["abono"] = abono, ["deposito_pagado"] = (double)pagado, ["deposito_saldo"] = (double)saldo };
    }

    public static async Task<Dictionary<string, object?>> EditarAbono(AuthUser u, string abonoId, JsonObject b)
    {
        if (!await DepositosRepository.AbonoDelPropietario(abonoId, u.Id)) throw new AppError("Abono no encontrado o no autorizado", 404);
        if (b.ContainsKey("monto"))
        {
            var m = Js.ParseFloat(b["monto"]);
            if (double.IsNaN(m) || m <= 0) throw new AppError("El monto del abono debe ser mayor a 0", 400);
        }
        Inputs.Fecha(b["fecha"], "fecha");
        return new() { ["abono"] = await DepositosRepository.Actualizar(abonoId, b["monto"], b["fecha"], b["metodo"], b["nota"]) };
    }

    public static async Task EliminarAbono(AuthUser u, string abonoId)
    {
        if (!await DepositosRepository.AbonoDelPropietario(abonoId, u.Id)) throw new AppError("Abono no encontrado o no autorizado", 404);
        await DepositosRepository.Eliminar(abonoId);
    }
}
