using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VertexRent.Api;

/// <summary>Resultado de confirmar/marcar un pago: si ya estaba pagado se avisa con <c>yaConfirmado</c>.</summary>
public sealed record ResultadoPago(Row Pago, bool YaConfirmado = false);

/// <summary>Reglas de negocio del módulo de pagos. No conoce HTTP; el SQL vive en <see cref="PagosRepository"/>.</summary>
public static class PagosService
{
    static string Metodo(Row inq) => string.IsNullOrEmpty(inq.S("metodo_pago")) ? "efectivo" : inq.S("metodo_pago")!;

    static Row ConDatos(Row pago, string? nombre, int depto) =>
        pago.With(("nombre_completo", nombre), ("depto_numero", depto));

    // ── Pago del periodo ─────────────────────────────────────────────────────
    /// <summary>Obtiene (o crea) el pago de un periodo con monto = solo renta (las cuotas extra se cobran aparte).</summary>
    public static async Task<Row> GetOrCrearPago(Row inquilino, string periodo) =>
        await PagosRepository.PagoDe(inquilino.S("id")!, periodo)
        ?? await PagosRepository.CrearPagoSiNoExiste(inquilino.S("id")!, periodo, inquilino.D("renta"), Metodo(inquilino));

    // ── QR del inquilino ─────────────────────────────────────────────────────
    public static async Task<Row> GenerarQr(AuthUser u, string inquilinoId)
    {
        // El QR lo genera únicamente el inquilino; admin/cobrador solo lo escanea.
        if (u.Rol != "inquilino") throw new AppError("Solo el inquilino puede generar su código QR de pago", 403);
        var periodo = Js.CurrentPeriodo();
        var inquilino = await PagosRepository.InquilinoActivoDelUsuario(inquilinoId, u.Id)
            ?? throw new AppError("Inquilino no encontrado o no autorizado", 403);

        var cuotas = await PagosRepository.ResumenCuotasPendientes(inquilinoId);
        var totalExtra = cuotas.D("total_extra");
        var cuotasDetalle = cuotas.J("cuotas") ?? new JsonArray();
        var renta = inquilino.D("renta"); // las cuotas extra se cobran aparte

        var existente = await PagosRepository.PagoDe(inquilinoId, periodo);
        Row pago;
        if (existente == null)
        {
            pago = await PagosRepository.CrearPagoSiNoExiste(inquilinoId, periodo, renta, Metodo(inquilino), Guid.NewGuid().ToString());
            if (string.IsNullOrEmpty(pago.S("qr_token")))
                pago = await PagosRepository.AsignarQr(pago.S("id")!, Guid.NewGuid().ToString()) ?? pago;
        }
        else if (string.IsNullOrEmpty(existente.S("qr_token")))
            pago = (await PagosRepository.AsignarQr(existente.S("id")!, Guid.NewGuid().ToString(), renta))!;
        else
            pago = await PagosRepository.RefrescarMonto(existente.S("id")!, renta) ?? (await PagosRepository.PagoDe(inquilinoId, periodo))!;

        return pago.With(
            ("renta_base", (double)inquilino.D("renta")),
            ("total_extra", (double)totalExtra),
            ("cuotas", cuotasDetalle),
            ("inquilino", new Dictionary<string, object?>
            {
                ["nombre_completo"] = inquilino.S("nombre_completo"),
                ["depto_numero"] = inquilino.I("depto_numero"),
            }));
    }

    public static async Task<Row> InfoPorToken(AuthUser u, string token) =>
        await PagosRepository.InfoPorToken(token, u.Id) ?? throw new AppError("QR inválido o expirado", 404);

    public static async Task<ResultadoPago> ConfirmarPorQr(AuthUser u, string token)
    {
        var pago = await PagosRepository.PagoPorToken(token, u.Id) ?? throw new AppError("QR inválido o expirado", 404);
        if (pago.B("confirmado")) return new(pago.Without("comprobante_url"), true);

        var pagoId = pago.S("id")!;
        await PagosRepository.MarcarEscaneo(pagoId, u.Actor);
        var actualizado = (await SaldoService.AbonarSaldoPendiente(pagoId, pago.S("metodo"), "Confirmado por escaneo de QR", u.Actor)).Pago;
        var data = ConDatos(actualizado, pago.S("nombre_completo"), pago.I("depto_numero"));
        AvisarConfirmado(pago.S("inquilino_usuario_id"), data, $"Tu pago de {pago.S("periodo")} fue confirmado");
        return new(data);
    }

    // ── Gestión por el propietario/colaborador ───────────────────────────────
    public static async Task<ResultadoPago> ConfirmarPorAdmin(AuthUser u, string pagoId)
    {
        var pago = await PagosRepository.PagoDelPropietario(pagoId, u.Id) ?? throw new AppError("Pago no encontrado o no autorizado", 404);
        if (pago.B("confirmado")) return new(pago, true);

        await PagosRepository.MarcarEscaneo(pagoId, u.Actor);
        var actualizado = (await SaldoService.AbonarSaldoPendiente(pagoId, pago.S("metodo"), "Confirmado por admin (comprobante)", u.Actor)).Pago;
        var data = ConDatos(actualizado, pago.S("nombre_completo"), pago.I("depto_numero"));
        AvisarConfirmado(pago.S("inquilino_usuario_id"), data, $"Tu pago de {pago.S("periodo")} fue confirmado");
        return new(data);
    }

    public static async Task<ResultadoPago> MarcarPagado(AuthUser u, string inquilinoId, bool soloRenta)
    {
        var periodo = Js.CurrentPeriodo();
        var inquilino = await InquilinosRepository.ActivoDelPropietario(inquilinoId, u.Id)
            ?? throw new AppError("Inquilino no encontrado o no autorizado", 404);

        var pago = await PagosRepository.PagoDe(inquilinoId, periodo)
            ?? await PagosRepository.CrearPagoSiNoExiste(inquilinoId, periodo, inquilino.D("renta"), Metodo(inquilino));
        if (pago.B("confirmado")) return new(pago, true);

        var pagoId = pago.S("id")!;
        await PagosRepository.MarcarEscaneo(pagoId, u.Actor);
        // "Solo renta": cobra la renta pero no los cargos extra pendientes del periodo.
        var nota = soloRenta
            ? "Marcado como pagado (solo renta, cargos extra pendientes) por admin"
            : "Marcado como pagado completo por admin";
        var actualizado = (await SaldoService.AbonarSaldoPendiente(
            pagoId, Metodo(inquilino), nota, u.Actor, soloRenta ? inquilino.D("renta") : null)).Pago;
        var data = ConDatos(actualizado, inquilino.S("nombre_completo"), inquilino.I("depto_numero"));

        var adminId = inquilino.S("admin_id");
        if (!string.IsNullOrEmpty(adminId))
            Js.Bg(() => PushService.NotificarEquipoPagos(adminId, "💵 Pago registrado",
                $"Depto {inquilino.S("depto_numero")} — {inquilino.S("nombre_completo")} · {periodo}"));
        AvisarConfirmado(inquilino.S("usuario_id"), data, $"Tu pago de {periodo} fue confirmado por el administrador");
        return new(data);
    }

    public static async Task<Row> SubirComprobante(AuthUser u, string inquilinoId, string imagen)
    {
        if (await InquilinosRepository.Accesible(inquilinoId, u) == null) throw new AppError("No autorizado", 403);

        var periodo = Js.CurrentPeriodo();
        var pago = await PagosRepository.PagoDe(inquilinoId, periodo)
            ?? await PagosRepository.CrearPagoSiNoExiste(inquilinoId, periodo, (await PagosRepository.RentaYMetodo(inquilinoId)).D("renta"), "transferencia");

        var actualizado = await PagosRepository.GuardarComprobante(pago.S("id")!, imagen)
            ?? throw new AppError("Pago ya confirmado o no encontrado", 400);

        var info = await PagosRepository.DatosParaAviso(inquilinoId);
        var adminId = info.S("admin_id");
        if (!string.IsNullOrEmpty(adminId))
        {
            SseService.EmitToUser(adminId, "comprobante_subido", new { pago = actualizado, inquilino = info });
            Js.Bg(() => PushService.NotificarEquipoPagos(adminId, "📎 Comprobante recibido",
                $"Depto {info.S("depto_numero")} — {info.S("nombre_completo")}"));
        }
        return actualizado;
    }

    public static async Task<Row> Rechazar(AuthUser u, string pagoId, string? comentario)
    {
        var pago = await PagosRepository.PagoDelPropietario(pagoId, u.Id) ?? throw new AppError("Pago no encontrado o no autorizado", 404);
        if (pago.B("confirmado")) throw new AppError("El pago ya fue confirmado y no puede rechazarse", 400);

        var actualizado = await PagosRepository.Rechazar(pagoId, string.IsNullOrEmpty(comentario) ? null : comentario);
        var data = ConDatos(actualizado, pago.S("nombre_completo"), pago.I("depto_numero"));

        var usuarioId = pago.S("inquilino_usuario_id");
        if (!string.IsNullOrEmpty(usuarioId))
        {
            SseService.EmitToUser(usuarioId, "payment_rejected", new { pago = data });
            var razon = !string.IsNullOrEmpty(comentario) ? $"Razón: {comentario}" : "Por favor envía un nuevo comprobante.";
            Js.Bg(() => PushService.CreateAndSendNotification(usuarioId, "❌ Comprobante rechazado",
                $"Tu comprobante de {pago.S("periodo")} fue rechazado. {razon}", "pago"));
        }
        return data;
    }

    /// <summary>Deshace un pago registrado por error; solo dentro de las 24 h siguientes.</summary>
    public static async Task Cancelar(AuthUser u, string pagoId)
    {
        var pago = await PagosRepository.PagoParaCancelar(pagoId, u.Id) ?? throw new AppError("Pago no encontrado o no autorizado", 404);
        var referencia = (pago.Dt("confirmado_en") ?? pago.Dt("created_at"))!.Value;
        if ((DateTime.UtcNow - referencia).TotalHours > 24)
            throw new AppError("Ya pasó más de un día. Para corregir este pago usa un abono manual.", 400);

        await PagosRepository.EliminarPago(pagoId);

        var usuarioId = pago.S("usuario_id");
        if (!string.IsNullOrEmpty(usuarioId))
        {
            SseService.EmitToUser(usuarioId, "payment_confirmed", new { pago = (object?)null });
            Js.Bg(() => PushService.CreateAndSendNotification(usuarioId, "Pago cancelado",
                $"El administrador canceló el registro de pago de {pago.S("periodo")}.", "pago"));
        }
    }

    // ── Consultas ────────────────────────────────────────────────────────────
    public static Task<List<Row>> ComprobantesPendientes(AuthUser u) => PagosRepository.ComprobantesPendientes(u.Id);

    public static Task<List<Row>> EstadosActuales(AuthUser u) => PagosRepository.EstadosActuales(Js.CurrentPeriodo(), u.Id);

    public static async Task<Dictionary<string, object?>> Estado(AuthUser u, string inquilinoId)
    {
        if (await InquilinosRepository.Accesible(inquilinoId, u) == null) throw new AppError("No autorizado", 403);
        var periodo = Js.CurrentPeriodo();
        var pago = await PagosRepository.PagoDe(inquilinoId, periodo);
        var cuotas = await PagosRepository.CuotasDelPeriodo(inquilinoId, periodo);

        var totalExtra = cuotas.Where(x => x.S("estado") == "pendiente").Sum(x => (double)x.D("monto"));
        var totalAbonado = pago != null ? (double)await PagosRepository.TotalAbonado(pago.S("id")!) : 0;
        return new()
        {
            ["data"] = pago, ["cuotas"] = cuotas, ["total_extra"] = totalExtra, ["total_abonado"] = totalAbonado,
            ["saldo_pendiente"] = pago != null ? Math.Max((double)pago.D("monto") - totalAbonado, 0) : 0,
            ["periodo"] = periodo,
        };
    }

    public static async Task<List<Row>> Historial(AuthUser u, string inquilinoId)
    {
        if (await InquilinosRepository.Accesible(inquilinoId, u) == null) throw new AppError("No autorizado", 403);
        return (await PagosRepository.Historial(inquilinoId)).Select(p =>
        {
            bool? aTiempo = null;
            if (p.B("confirmado") && !string.IsNullOrEmpty(p.S("confirmado_en")) && !string.IsNullOrEmpty(p.S("fecha_pago")))
            {
                var confirmado = p.Dt("confirmado_en")!.Value;
                var partes = p.S("periodo")!.Split('-');
                var diaPago = Js.ParseInt(p.S("fecha_pago"));
                var gracia = Js.ParseInt(p.S("dias_gracia")) ?? 0;
                if (diaPago == null) aTiempo = false; // día de pago ilegible: no se puede afirmar que fue a tiempo
                else
                {
                    var limite = new DateTime(int.Parse(partes[0]), int.Parse(partes[1]), 1, 23, 59, 59, DateTimeKind.Local).AddDays(diaPago.Value + gracia - 1);
                    aTiempo = confirmado <= limite.ToUniversalTime();
                }
            }
            return p.Without("dias_gracia").With(("a_tiempo", aTiempo));
        }).ToList();
    }

    /// <summary>La imagen de un comprobante se pide aparte: los listados solo indican si existe (tiene_comprobante).</summary>
    public static async Task<Dictionary<string, object?>> Comprobante(AuthUser u, string pagoId)
    {
        if (!await PagosRepository.PagoAccesible(pagoId, u)) throw new AppError("No autorizado", 403);
        var imagen = await PagosRepository.ImagenDelComprobante(pagoId) ?? throw new AppError("Este pago no tiene comprobante", 404);
        return new() { ["comprobante_url"] = imagen };
    }

    // ── Abonos ───────────────────────────────────────────────────────────────
    public static async Task<List<Row>> Abonos(AuthUser u, string pagoId)
    {
        if (!await PagosRepository.PagoAccesible(pagoId, u)) throw new AppError("No autorizado", 403);
        return await PagosRepository.Abonos(pagoId);
    }

    static Dictionary<string, object?> ResultadoAbono(Row? abono, RecalculoPago rec)
    {
        var d = new Dictionary<string, object?>();
        if (abono != null) d["abono"] = abono;
        d["pago"] = rec.Pago; d["totalAbonado"] = (double)rec.TotalAbonado; d["saldoPendiente"] = (double)rec.SaldoPendiente;
        return d;
    }

    public static async Task<Dictionary<string, object?>> RegistrarAbono(AuthUser u, JsonObject b)
    {
        if (!Js.T(b["inquilino_id"])) throw new AppError("inquilino_id es requerido", 400);
        var monto = Js.ParseFloat(b["monto"]);
        if (double.IsNaN(monto) || monto <= 0) throw new AppError("El monto del abono debe ser mayor a 0", 400);

        var inquilino = await InquilinosRepository.ActivoDelPropietario(b.S("inquilino_id"), u.Id)
            ?? throw new AppError("Inquilino no encontrado o no autorizado", 404);

        // Sin periodo y con la bandera, el abono paga primero el mes más antiguo con adeudo (y reparte el sobrante).
        var aDeudaAntigua = b["aplicar_a_deuda_antigua"] is JsonValue fv && fv.TryGetValue<bool>(out var f) && f;
        if (aDeudaAntigua && !Js.T(b["periodo"])) return await RegistrarAbonoADeudaAntigua(u, b, inquilino, (decimal)monto);

        var periodo = Js.T(b["periodo"]) ? b.S("periodo")! : Js.CurrentPeriodo();
        if (!Inputs.EsPeriodo(periodo)) throw new AppError("El periodo debe tener el formato AAAA-MM", 400);
        var pago = await GetOrCrearPago(inquilino, periodo);
        if (pago.B("confirmado")) throw new AppError("Este periodo ya está pagado por completo", 400);

        var (abono, rec) = await SaldoService.RegistrarAbono(
            pago.S("id")!, (decimal)monto, Inputs.Fecha(b["fecha"], "fecha"), Js.T(b["metodo"]) ? b.S("metodo") : Metodo(inquilino),
            Js.T(b["nota"]) ? b.S("nota") : null, Inputs.Imagen(b["comprobante_url"], "comprobante_url"), u.Actor);

        var data = ConDatos(rec.Pago, inquilino.S("nombre_completo"), inquilino.I("depto_numero"));
        var usuarioId = inquilino.S("usuario_id");
        if (!string.IsNullOrEmpty(usuarioId))
        {
            if (rec.Completo) AvisarConfirmado(usuarioId, data, $"Tu pago de {periodo} fue confirmado");
            else
                Js.Bg(() => PushService.CreateAndSendNotification(usuarioId, "💰 Abono registrado",
                    $"Se registró un abono de ${Js.NumMx(monto)} a tu renta de {periodo}. Saldo pendiente: ${Js.NumMx((double)rec.SaldoPendiente)}", "pago"));
        }
        return ResultadoAbono(abono, rec);
    }

    /// <summary>
    /// Aplica un abono empezando por el mes más antiguo con adeudo y pasando al siguiente con lo que sobre (un abono al mes
    /// actual no rebaja un atraso anterior). Si supera el adeudo total se rechaza sin aplicar nada.
    /// </summary>
    static async Task<Dictionary<string, object?>> RegistrarAbonoADeudaAntigua(AuthUser u, JsonObject b, Row inquilino, decimal monto)
    {
        var inquilinoId = inquilino.S("id")!;
        var datos = await InquilinosRepository.Accesible(inquilinoId, u, "id, renta, fecha_inicio::text AS fecha_inicio_txt")
            ?? throw new AppError("Inquilino no encontrado o no autorizado", 404);
        var pendientes = (await PagosRepository.SaldoDetalle(inquilinoId, datos.S("fecha_inicio_txt"), datos.S("renta")))
            .Where(r => r.D("saldo") > 0.005m).ToList();
        if (pendientes.Count == 0) throw new AppError("No hay adeudo pendiente: el inquilino está al corriente", 400);
        var deuda = pendientes.Sum(r => r.D("saldo"));
        if (monto > deuda + 0.01m)
            throw new AppError($"El abono (${Js.NumMx((double)monto)}) supera el adeudo total (${Js.NumMx((double)deuda)})", 400);

        var fecha = Inputs.Fecha(b["fecha"], "fecha");
        var metodo = Js.T(b["metodo"]) ? b.S("metodo") : Metodo(inquilino);
        var nota = Js.T(b["nota"]) ? b.S("nota") : null;
        var imagen = Inputs.Imagen(b["comprobante_url"], "comprobante_url");
        var usuarioId = inquilino.S("usuario_id");

        var restante = monto;
        var aplicado = new List<object?>();
        Row? ultimoAbono = null; RecalculoPago? ultimo = null;
        foreach (var fila in pendientes)
        {
            if (restante <= 0.005m) break;
            var parte = Math.Min(restante, fila.D("saldo"));
            var periodo = fila.S("periodo")!;
            var pago = await GetOrCrearPago(inquilino, periodo);
            if (pago.B("confirmado")) continue;
            var (abono, rec) = await SaldoService.RegistrarAbono(pago.S("id")!, parte, fecha, metodo, nota, imagen, u.Actor);
            restante -= parte; ultimoAbono = abono; ultimo = rec;
            aplicado.Add(new Dictionary<string, object?> { ["periodo"] = periodo, ["monto"] = (double)parte });
            if (rec.Completo) AvisarConfirmado(usuarioId, ConDatos(rec.Pago, inquilino.S("nombre_completo"), inquilino.I("depto_numero")), $"Tu pago de {periodo} fue confirmado");
        }

        if (ultimo is null) throw new AppError("No se pudo aplicar el abono", 400);
        if (!string.IsNullOrEmpty(usuarioId) && !ultimo.Completo)
            Js.Bg(() => PushService.CreateAndSendNotification(usuarioId, "💰 Abono registrado",
                $"Se registró un abono de ${Js.NumMx((double)monto)} a tu renta. Saldo pendiente total: ${Js.NumMx((double)(deuda - monto))}", "pago"));

        var resultado = ResultadoAbono(ultimoAbono, ultimo);
        resultado["aplicado"] = aplicado;
        return resultado;
    }

    public static async Task<Dictionary<string, object?>> EditarAbono(AuthUser u, string abonoId, JsonObject b)
    {
        var pagoId = await PagosRepository.PagoDelAbono(abonoId, u.Id) ?? throw new AppError("Abono no encontrado o no autorizado", 404);
        if (b.ContainsKey("monto"))
        {
            var m = Js.ParseFloat(b["monto"]);
            if (double.IsNaN(m) || m <= 0) throw new AppError("El monto del abono debe ser mayor a 0", 400);
        }
        Inputs.Fecha(b["fecha"], "fecha");
        var abono = await PagosRepository.ActualizarAbono(abonoId, b["monto"], b["fecha"], b["metodo"], b["nota"]);
        return ResultadoAbono(abono, await SaldoService.RecalcularPago(pagoId));
    }

    public static async Task<Dictionary<string, object?>> EliminarAbono(AuthUser u, string abonoId)
    {
        var pagoId = await PagosRepository.PagoDelAbono(abonoId, u.Id) ?? throw new AppError("Abono no encontrado o no autorizado", 404);
        await PagosRepository.EliminarAbono(abonoId);
        return ResultadoAbono(null, await SaldoService.RecalcularPago(pagoId));
    }

    // ── Promesas de pago ─────────────────────────────────────────────────────
    static string? NormalizarFechaPromesa(JsonNode? raw)
    {
        var s = Js.Str(raw)?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        var dmy = Regex.Match(s, @"^(\d{1,2})[/\-](\d{1,2})[/\-](\d{4})$");
        if (dmy.Success) s = $"{dmy.Groups[3].Value}-{dmy.Groups[2].Value.PadLeft(2, '0')}-{dmy.Groups[1].Value.PadLeft(2, '0')}";
        if (!Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}$"))
            throw new AppError("La fecha debe tener el formato DD/MM/AAAA o AAAA-MM-DD", 400);
        return s;
    }

    public static async Task<Row?> SetPromesa(AuthUser u, string pagoId, JsonNode? fechaCruda)
    {
        var fecha = NormalizarFechaPromesa(fechaCruda);
        if (!await PagosRepository.PagoDelPropietarioExiste(pagoId, u.Id)) throw new AppError("Pago no encontrado o no autorizado", 404);
        return await PagosRepository.GuardarPromesa(pagoId, fecha, u.Actor);
    }

    public static async Task<Row?> SetPromesaInquilino(AuthUser u, string inquilinoId, JsonObject b)
    {
        var fecha = NormalizarFechaPromesa(b["fecha_promesa"]);
        var per = Js.T(b["periodo"]) ? b.S("periodo") : null;
        var periodo = Inputs.EsPeriodo(per) ? per! : Js.CurrentPeriodo();

        var inquilino = await InquilinosRepository.ActivoDelPropietario(inquilinoId, u.Id)
            ?? throw new AppError("Inquilino no encontrado o no autorizado", 404);
        var existente = await PagosRepository.PagoDe(inquilinoId, periodo);
        // Borrar la promesa cuando ni siquiera hay pago: no hay nada que guardar.
        if (fecha == null && existente == null) return null;

        var pago = existente ?? await GetOrCrearPago(inquilino, periodo);
        if (pago.B("confirmado")) throw new AppError("Este periodo ya está pagado por completo", 400);
        return await PagosRepository.GuardarPromesa(pago.S("id")!, fecha, u.Actor);
    }

    public static async Task<List<Row>> PromesasHistorial(AuthUser u, string inquilinoId) =>
        (await PagosRepository.Promesas(inquilinoId, u)).Select(row =>
        {
            var estado = "pendiente";
            if (row.B("confirmado") && !string.IsNullOrEmpty(row.S("confirmado_en")))
            {
                // La fecha prometida es de calendario CDMX: se compara el día CDMX del pago, no el día UTC.
                var pagado = TimeZoneInfo.ConvertTime(row.Dt("confirmado_en")!.Value, Js.Cdmx).ToString("yyyy-MM-dd");
                estado = string.CompareOrdinal(pagado, row.S("fecha_promesa")![..10]) <= 0 ? "cumplida" : "incumplida";
            }
            return row.With(("estado_cumplimiento", estado));
        }).ToList();

    public static async Task EliminarPromesa(AuthUser u, string id)
    {
        if (!await PagosRepository.EliminarPromesa(id, u.Id)) throw new AppError("Registro no encontrado o no autorizado", 404);
    }

    // ── Deuda (saldos) ───────────────────────────────────────────────────────
    public static Task<List<Row>> Saldos(AuthUser u) => PagosRepository.Saldos(u.Id);

    public static async Task<Dictionary<string, object?>> ResumenDeuda(AuthUser u)
    {
        var filas = await PagosRepository.ResumenSaldos(u.Id);
        // por_departamento y total_general = solo atraso real; total_por_cobrar = todo lo pendiente del ciclo.
        return new()
        {
            ["total_general"] = filas.Sum(x => (double)x.D("deuda_vencida")),
            ["total_por_cobrar"] = filas.Sum(x => (double)x.D("deuda_total")),
            ["por_departamento"] = filas.Where(x => (double)x.D("deuda_vencida") > 0).ToList(),
        };
    }

    public static async Task<Dictionary<string, object?>> SaldoInquilino(AuthUser u, string inquilinoId)
    {
        var inq = await InquilinosRepository.Accesible(inquilinoId, u, "id, renta, fecha_inicio::text AS fecha_inicio_txt")
            ?? throw new AppError("No autorizado", 403);
        var detalle = await PagosRepository.SaldoDetalle(inquilinoId, inq.S("fecha_inicio_txt"), inq.S("renta"));
        return new() { ["deuda_total"] = detalle.Sum(r => (double)r.D("saldo")), ["detalle"] = detalle };
    }

    // ── Avisos ───────────────────────────────────────────────────────────────
    static void AvisarConfirmado(string? usuarioId, Row pagoData, string mensaje)
    {
        if (string.IsNullOrEmpty(usuarioId)) return;
        SseService.EmitToUser(usuarioId, "payment_confirmed", new { pago = pagoData });
        Js.Bg(() => PushService.CreateAndSendNotification(usuarioId, "✅ ¡Pago confirmado!", mensaje, "pago"));
    }
}
