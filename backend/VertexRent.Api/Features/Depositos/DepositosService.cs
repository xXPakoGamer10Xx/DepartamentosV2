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

    /// <summary>
    /// Opción 4 (Pago semanal): aplica primero a los periodos de renta pendientes (del más antiguo al actual)
    /// y destina el excedente al depósito del inquilino dentro de una única transacción bloqueada.
    /// </summary>
    public static async Task<Dictionary<string, object?>> RegistrarAbonoSemanal(AuthUser u, JsonObject b)
    {
        if (!Js.T(b["inquilino_id"])) throw new AppError("inquilino_id es requerido", 400);
        var inquilinoId = b.S("inquilino_id")!;
        var fecha = Inputs.Fecha(b["fecha"], "fecha");
        var imagen = Inputs.Imagen(b["comprobante_url"], "comprobante_url");

        decimal? montoPedido = null;
        if (b.TryGetPropertyValue("monto", out var montoNode) && montoNode is not null && !string.IsNullOrWhiteSpace(Js.Str(montoNode)))
        {
            var m = Js.ParseFloat(montoNode);
            if (double.IsNaN(m) || double.IsInfinity(m) || m <= 0)
                throw new AppError("El monto del abono debe ser mayor a 0", 400);
            montoPedido = Math.Round((decimal)m, 2, MidpointRounding.AwayFromZero);
        }

        var notaUsuario = Js.T(b["nota"]) ? b.S("nota")!.Trim() : null;

        var res = await Db.Tx(async tx =>
        {
            var inq = await DepositosRepository.InquilinoActivoBloqueado(tx, inquilinoId, u.Id)
                ?? throw new AppError("Inquilino no encontrado o no autorizado", 404);

            var monto = montoPedido ?? (inq.D("deposito_monto_semanal") > 0m ? inq.D("deposito_monto_semanal") : 1000m);
            if (monto <= 0m) throw new AppError("El monto del abono debe ser mayor a 0", 400);

            var renta = inq.D("renta");
            var metodo = Js.T(b["metodo"])
                ? b.S("metodo")!
                : (string.IsNullOrEmpty(inq.S("metodo_pago")) ? "efectivo" : inq.S("metodo_pago")!);

            var pendientes = await DepositosRepository.PeriodosRentaPendientesEnTx(tx, inquilinoId, inq.S("fecha_inicio_txt")!, renta);
            var depPagado = await DepositosRepository.TotalPagado(tx, inquilinoId);
            var depTotal = inq.D("deposito");
            var depSaldo = Math.Max(depTotal - depPagado, 0m);
            var deudaRenta = pendientes.Sum(r => r.D("saldo"));
            var deudaCombinada = deudaRenta + depSaldo;

            if (deudaCombinada <= 0.005m)
                throw new AppError("La renta del periodo actual y el depósito ya están cubiertos por completo", 400);
            if (monto > deudaCombinada + 0.01m)
                throw new AppError($"El abono (${Js.NumMx((double)monto)}) supera el saldo pendiente de renta y depósito (${Js.NumMx((double)deudaCombinada)})", 400);

            var restante = monto;
            var aplicadoRenta = new List<Dictionary<string, object?>>();
            var totalRentaAplicada = 0m;
            var notaRenta = string.IsNullOrEmpty(notaUsuario) ? "Abono semanal (aplicado a renta)" : $"Abono semanal (renta) — {notaUsuario}";
            var notaDep = string.IsNullOrEmpty(notaUsuario) ? "Abono semanal (excedente aplicado a depósito)" : $"Abono semanal (depósito) — {notaUsuario}";

            foreach (var fila in pendientes)
            {
                if (restante <= 0.005m) break;
                var periodo = fila.S("periodo")!;
                var pago = await DepositosRepository.AsegurarPagoEnTx(tx, inquilinoId, periodo, renta, metodo);
                if (pago.B("confirmado")) continue;

                var pagoId = pago.S("id")!;
                var saldoReal = Math.Max(pago.D("monto") - await SaldoRepository.TotalAbonado(tx, pagoId), 0m);
                if (saldoReal <= 0.005m) continue;

                var parte = Math.Round(Math.Min(restante, saldoReal), 2, MidpointRounding.AwayFromZero);
                if (parte <= 0m) continue;

                await SaldoRepository.InsertarAbono(tx, pagoId, parte, fecha, metodo, notaRenta, imagen, u.Actor);
                var rec = await SaldoService.RecalcularPago(pagoId, tx);
                restante -= parte;
                totalRentaAplicada += parte;
                aplicadoRenta.Add(new Dictionary<string, object?>
                {
                    ["periodo"] = periodo,
                    ["monto"] = (double)parte,
                    ["completo"] = rec.Completo,
                });
            }

            var aplicadoDep = 0m;
            if (restante > 0.005m && depSaldo > 0.005m)
            {
                var parteDep = Math.Round(Math.Min(restante, depSaldo), 2, MidpointRounding.AwayFromZero);
                if (parteDep > 0m)
                {
                    await DepositosRepository.Insertar(tx, inquilinoId, parteDep, fecha, metodo, notaDep, imagen, u.Actor);
                    aplicadoDep = parteDep;
                    depPagado += parteDep;
                    depSaldo = Math.Max(depTotal - depPagado, 0m);
                }
            }

            return (
                UsuarioId: inq.S("usuario_id"),
                Monto: monto,
                AplicadoRenta: aplicadoRenta,
                TotalRentaAplicada: totalRentaAplicada,
                AplicadoDep: aplicadoDep,
                DepPagado: depPagado,
                DepSaldo: depSaldo
            );
        });

        if (!string.IsNullOrEmpty(res.UsuarioId))
        {
            var mensaje = (res.TotalRentaAplicada > 0m, res.AplicadoDep > 0m) switch
            {
                (true, true) => $"Se registró tu pago semanal de ${Js.NumMx((double)res.Monto)}: ${Js.NumMx((double)res.TotalRentaAplicada)} a renta y ${Js.NumMx((double)res.AplicadoDep)} a depósito. Saldo de depósito: ${Js.NumMx((double)res.DepSaldo)}",
                (true, false) => $"Se registró tu pago semanal de ${Js.NumMx((double)res.Monto)} aplicado a renta. Saldo de depósito: ${Js.NumMx((double)res.DepSaldo)}",
                _ => $"Se registró tu pago semanal de ${Js.NumMx((double)res.Monto)} aplicado a tu depósito. Saldo pendiente de depósito: ${Js.NumMx((double)res.DepSaldo)}",
            };
            Js.Bg(() => PushService.CreateAndSendNotification(res.UsuarioId, "💰 Pago semanal registrado", mensaje, "pago"));
        }

        return new()
        {
            ["aplicado_renta"] = res.AplicadoRenta,
            ["total_aplicado_renta"] = (double)res.TotalRentaAplicada,
            ["aplicado_deposito"] = (double)res.AplicadoDep,
            ["deposito_pagado"] = (double)res.DepPagado,
            ["deposito_saldo"] = (double)res.DepSaldo,
        };
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
