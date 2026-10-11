using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VertexRent.Api;

public static class PagosEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/pagos");

        // Rutas públicas (softAuth registra quién escaneó si hay sesión). tokenLimiter evita enumerar tokens.
        g.MapGet("/info/{token}", H(GetPagoByToken)).Limit(RateLimits.Token, "token");
        g.MapPost("/confirmar/{token}", H(ConfirmarPago)).Limit(RateLimits.Token, "token").SoftAuth();

        var a = g.MapGroup("").Auth();

        a.MapPost("/generar-qr/{inquilino_id}", H(GenerarQr));
        a.MapGet("/estados-actuales", H(EstadosActuales)).Permiso("pagos");
        a.MapGet("/estado/{inquilino_id}", H(EstadoPago));
        a.MapPost("/comprobante/{inquilino_id}", H(SubirComprobante));
        a.MapPost("/confirmar-admin/{pago_id}", H(ConfirmarPagoAdmin)).Permiso("pagos.marcar");
        a.MapPost("/marcar-pagado/{inquilino_id}", H(MarcarPagadoAdmin)).Permiso("pagos.marcar");
        a.MapDelete("/{pago_id}/cancelar", H(CancelarPago)).Permiso("pagos.marcar");
        a.MapPost("/rechazar-admin/{pago_id}", H(RechazarPagoAdmin)).Permiso("pagos.marcar");
        a.MapGet("/comprobantes-pendientes", H(ComprobantesPendientes)).Permiso("pagos");
        a.MapGet("/historial/{inquilino_id}", H(HistorialPagos));

        // Abonos y saldo/deuda acumulada
        a.MapPost("/abono", H(RegistrarAbono)).Permiso("pagos.marcar");
        a.MapGet("/abonos/{pago_id}", H(AbonosPago));
        a.MapPut("/abono/{abono_id}", H(EditarAbono)).Permiso("pagos.marcar");
        a.MapDelete("/abono/{abono_id}", H(EliminarAbono)).Permiso("pagos.marcar");
        a.MapGet("/saldos/resumen", H(ResumenDeuda)).CobradorOrAdmin();
        a.MapGet("/saldos", H(SaldosInquilinos)).CobradorOrAdmin();
        a.MapGet("/saldo/{inquilino_id}", H(SaldoInquilino));
        a.MapPut("/promesa/inquilino/{inquilino_id}", H(SetPromesaInquilino)).Permiso("pagos.marcar");
        a.MapPut("/{pago_id}/promesa", H(SetPromesa)).Permiso("pagos.marcar");
        a.MapGet("/promesas/{inquilino_id}", H(PromesasHistorial));
        a.MapDelete("/promesas/{id}", H(EliminarPromesa)).Permiso("pagos.marcar");
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    static async Task<decimal> TotalExtraPendiente(string inquilinoId) =>
        (await Db.Query("SELECT COALESCE(SUM(monto), 0) as total_extra FROM cuotas_extra WHERE inquilino_id = $1 AND estado = 'pendiente'", inquilinoId)).First!.D("total_extra");

    static async Task<decimal> TotalAbonado(string pagoId) =>
        (await Db.Query("SELECT COALESCE(SUM(monto), 0) AS total FROM abonos_pago WHERE pago_id = $1", pagoId)).First!.D("total");

    /// <summary>Obtiene (o crea) el pago de un periodo con monto = renta + cuotas extra pendientes.</summary>
    public static async Task<Row> GetOrCrearPago(Row inquilino, string periodo)
    {
        var existing = await Db.Query("SELECT * FROM pagos WHERE inquilino_id = $1 AND periodo = $2", inquilino.S("id"), periodo);
        if (existing.First != null) return existing.First;

        var montoTotal = inquilino.D("renta") + await TotalExtraPendiente(inquilino.S("id")!);
        var ins = await Db.Query(
            "INSERT INTO pagos (inquilino_id, periodo, monto, metodo) VALUES ($1, $2, $3, $4) RETURNING *",
            inquilino.S("id"), periodo, montoTotal, Js.T(JsonValue.Create(inquilino.S("metodo_pago"))) ? inquilino.S("metodo_pago") : "efectivo");
        return ins.First!;
    }

    static string Metodo(Row inq) => string.IsNullOrEmpty(inq.S("metodo_pago")) ? "efectivo" : inq.S("metodo_pago")!;

    static Row PagoData(Row pago, string? nombre, string? depto) =>
        pago.With(("nombre_completo", nombre), ("depto_numero", depto is null ? null : int.Parse(depto)));

    // ── POST /generar-qr/:inquilino_id ───────────────────────────────────────
    static async Task<IResult> GenerarQr(HttpContext c)
    {
        var u = c.Me();
        // El QR lo genera únicamente el inquilino; admin/cobrador solo lo escanea.
        if (u.Rol != "inquilino") throw new AppError("Solo el inquilino puede generar su código QR de pago", 403);

        var inquilinoId = Api.Route(c, "inquilino_id");
        var periodo = Js.CurrentPeriodo();

        var inqRes = await Db.Query(
            "SELECT id, nombre_completo, depto_numero, renta, metodo_pago FROM inquilinos WHERE id = $1 AND usuario_id = $2 AND estado = 'activo'",
            inquilinoId, u.Id);
        var inquilino = inqRes.First ?? throw new AppError("Inquilino no encontrado o no autorizado", 403);

        var cuotasRes = await Db.Query(
            @"SELECT COALESCE(SUM(monto), 0) as total_extra,
                     json_agg(json_build_object('id', id, 'concepto', concepto, 'monto', monto)) FILTER (WHERE id IS NOT NULL) as cuotas
              FROM cuotas_extra WHERE inquilino_id = $1 AND estado = 'pendiente'", inquilinoId);
        var totalExtra = cuotasRes.First!.D("total_extra");
        var cuotasDetalle = cuotasRes.First.J("cuotas") ?? new JsonArray();
        var montoTotal = inquilino.D("renta") + totalExtra;

        var pagoRes = await Db.Query("SELECT * FROM pagos WHERE inquilino_id = $1 AND periodo = $2", inquilinoId, periodo);
        Row pago;
        if (pagoRes.First == null)
        {
            pago = (await Db.Query(
                "INSERT INTO pagos (inquilino_id, periodo, monto, metodo, qr_token) VALUES ($1, $2, $3, $4, $5) RETURNING *",
                inquilinoId, periodo, montoTotal, Metodo(inquilino), Guid.NewGuid().ToString())).First!;
        }
        else if (string.IsNullOrEmpty(pagoRes.First.S("qr_token")))
        {
            pago = (await Db.Query("UPDATE pagos SET qr_token = $1, monto = $2 WHERE id = $3 RETURNING *",
                Guid.NewGuid().ToString(), montoTotal, pagoRes.First.S("id"))).First!;
        }
        else
        {
            // Refrescar monto por si cambiaron las cuotas
            var upd = await Db.Query("UPDATE pagos SET monto = $1 WHERE id = $2 AND confirmado = false RETURNING *",
                montoTotal, pagoRes.First.S("id"));
            pago = upd.First ?? (await Db.Query("SELECT * FROM pagos WHERE inquilino_id = $1 AND periodo = $2", inquilinoId, periodo)).First!;
        }

        return Api.Data(pago.With(
            ("renta_base", (double)inquilino.D("renta")),
            ("total_extra", (double)totalExtra),
            ("cuotas", cuotasDetalle),
            ("inquilino", new Dictionary<string, object?>
            {
                ["nombre_completo"] = inquilino.S("nombre_completo"),
                ["depto_numero"] = inquilino.I("depto_numero"),
            })));
    }

    // ── GET /info/:token ─────────────────────────────────────────────────────
    static async Task<IResult> GetPagoByToken(HttpContext c)
    {
        var r = await Db.Query(
            @"SELECT p.*, i.nombre_completo, i.depto_numero, i.renta
              FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE p.qr_token = $1", Api.Route(c, "token"));
        if (r.First == null) throw new AppError("QR inválido o expirado", 404);
        return Api.Data(r.First);
    }

    // ── POST /confirmar/:token ───────────────────────────────────────────────
    static async Task<IResult> ConfirmarPago(HttpContext c)
    {
        var token = Api.Route(c, "token");
        var pagoRes = await Db.Query(
            @"SELECT p.*, i.nombre_completo, i.depto_numero, i.admin_id
              FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE p.qr_token = $1", token);
        var pago = pagoRes.First ?? throw new AppError("QR inválido o expirado", 404);

        if (pago.B("confirmado"))
            return Api.Obj(200, ("data", pago), ("yaConfirmado", true));

        var body = await Api.Body(c);
        var meId = c.MaybeMe()?.Id;
        var scannerId = !string.IsNullOrEmpty(meId) ? meId : (Js.T(body["scanner_id"]) ? body.S("scanner_id") : null);

        await Db.Query("UPDATE pagos SET escaneado_por = $2, escaneado_en = NOW() WHERE id = $1", pago.S("id"), scannerId);

        var saldoPendiente = Math.Max(pago.D("monto") - await TotalAbonado(pago.S("id")!), 0);
        if (saldoPendiente > 0)
            await Db.Query(
                "INSERT INTO abonos_pago (pago_id, monto, metodo, nota, registrado_por) VALUES ($1, $2, $3, $4, $5)",
                pago.S("id"), saldoPendiente, pago.S("metodo"), "Confirmado por escaneo de QR", scannerId);
        var actualizado = (await SaldoService.RecalcularPago(pago.S("id")!)).Pago;

        var pagoData = actualizado.With(("nombre_completo", pago.S("nombre_completo")), ("depto_numero", pago.I("depto_numero")));

        Js.Bg(async () =>
        {
            var inq = await Db.Query("SELECT usuario_id FROM inquilinos WHERE id = $1", pago.S("inquilino_id"));
            var usuarioId = inq.First?.S("usuario_id");
            if (!string.IsNullOrEmpty(usuarioId))
            {
                SseService.EmitToUser(usuarioId, "payment_confirmed", new { pago = pagoData });
                await PushService.CreateAndSendNotification(usuarioId, "✅ ¡Pago confirmado!", $"Tu pago de {pago.S("periodo")} fue confirmado", "pago");
            }
        });

        return Api.Data(pagoData);
    }

    // ── POST /rechazar-admin/:pago_id ────────────────────────────────────────
    static async Task<IResult> RechazarPagoAdmin(HttpContext c)
    {
        var pagoId = Api.Route(c, "pago_id");
        var b = await Api.Body(c);
        var comentario = b["comentario"] is JsonValue v && v.TryGetValue<string>(out var cs) ? cs.Trim() : null;

        var pagoRes = await Db.Query(
            @"SELECT p.*, i.nombre_completo, i.depto_numero, i.usuario_id as inquilino_usuario_id, i.admin_id
              FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE p.id = $1 AND i.admin_id = $2", pagoId, c.Me().Id);
        var pago = pagoRes.First ?? throw new AppError("Pago no encontrado o no autorizado", 404);
        if (pago.B("confirmado")) throw new AppError("El pago ya fue confirmado y no puede rechazarse", 400);

        var updated = await Db.Query(
            "UPDATE pagos SET rechazado = true, rechazado_en = NOW(), comentario_admin = $2 WHERE id = $1 RETURNING *",
            pagoId, string.IsNullOrEmpty(comentario) ? null : comentario);

        var pagoData = updated.First!.With(("nombre_completo", pago.S("nombre_completo")), ("depto_numero", pago.I("depto_numero")));

        var usuarioId = pago.S("inquilino_usuario_id");
        if (!string.IsNullOrEmpty(usuarioId))
        {
            SseService.EmitToUser(usuarioId, "payment_rejected", new { pago = pagoData });
            var razon = !string.IsNullOrEmpty(comentario) ? $"Razón: {comentario}" : "Por favor envía un nuevo comprobante.";
            Js.Bg(() => PushService.CreateAndSendNotification(usuarioId, "❌ Comprobante rechazado",
                $"Tu comprobante de {pago.S("periodo")} fue rechazado. {razon}", "pago"));
        }
        return Api.Data(pagoData);
    }

    // ── POST /comprobante/:inquilino_id ──────────────────────────────────────
    static async Task<IResult> SubirComprobante(HttpContext c)
    {
        var inquilinoId = Api.Route(c, "inquilino_id");
        var b = await Api.Body(c);
        var url = b["comprobante_url"];
        if (!Js.T(url)) throw new AppError("comprobante_url es requerido", 400);

        var u = c.Me();
        var authCheck = u.Rol == "inquilino" ? "AND usuario_id = $2" : "AND admin_id = $2";
        var own = await Db.Query($"SELECT id FROM inquilinos WHERE id = $1 {authCheck}", inquilinoId, u.Id);
        if (own.First == null) throw new AppError("No autorizado", 403);

        var periodo = Js.CurrentPeriodo();
        var pagoRes = await Db.Query("SELECT * FROM pagos WHERE inquilino_id = $1 AND periodo = $2", inquilinoId, periodo);
        Row pago;
        if (pagoRes.First == null)
        {
            var inq = (await Db.Query("SELECT renta, metodo_pago FROM inquilinos WHERE id = $1", inquilinoId)).First!;
            var montoTotal = inq.D("renta") + await TotalExtraPendiente(inquilinoId);
            pago = (await Db.Query(
                "INSERT INTO pagos (inquilino_id, periodo, monto, metodo) VALUES ($1, $2, $3, $4) RETURNING *",
                inquilinoId, periodo, montoTotal, "transferencia")).First!;
        }
        else pago = pagoRes.First;

        var updated = await Db.Query(
            @"UPDATE pagos SET comprobante_url = $1, comprobante_subido_en = NOW(),
                rechazado = false, rechazado_en = NULL, comentario_admin = NULL
              WHERE id = $2 AND confirmado = false RETURNING *", url, pago.S("id"));
        if (updated.First == null) throw new AppError("Pago ya confirmado o no encontrado", 400);

        var inqInfo = (await Db.Query("SELECT nombre_completo, depto_numero, admin_id FROM inquilinos WHERE id = $1", inquilinoId)).First!;
        var adminId = inqInfo.S("admin_id");
        if (!string.IsNullOrEmpty(adminId))
        {
            SseService.EmitToUser(adminId, "comprobante_subido", new { pago = updated.First, inquilino = inqInfo });
            Js.Bg(() => PushService.NotificarEquipoPagos(adminId, "📎 Comprobante recibido",
                $"Depto {inqInfo.S("depto_numero")} — {inqInfo.S("nombre_completo")}"));
        }
        return Api.Data(updated.First);
    }

    // ── POST /confirmar-admin/:pago_id ───────────────────────────────────────
    static async Task<IResult> ConfirmarPagoAdmin(HttpContext c)
    {
        var pagoId = Api.Route(c, "pago_id");
        var u = c.Me();
        var pagoRes = await Db.Query(
            @"SELECT p.*, i.nombre_completo, i.depto_numero, i.usuario_id as inquilino_usuario_id, i.admin_id
              FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE p.id = $1 AND i.admin_id = $2", pagoId, u.Id);
        var pago = pagoRes.First ?? throw new AppError("Pago no encontrado o no autorizado", 404);

        if (pago.B("confirmado")) return Api.Obj(200, ("data", pago), ("yaConfirmado", true));

        await Db.Query("UPDATE pagos SET escaneado_por = $2, escaneado_en = NOW() WHERE id = $1", pagoId, u.Actor);

        var saldoPendiente = Math.Max(pago.D("monto") - await TotalAbonado(pagoId), 0);
        if (saldoPendiente > 0)
            await Db.Query(
                "INSERT INTO abonos_pago (pago_id, monto, metodo, nota, registrado_por) VALUES ($1, $2, $3, $4, $5)",
                pagoId, saldoPendiente, pago.S("metodo"), "Confirmado por admin (comprobante)", u.Actor);
        var actualizado = (await SaldoService.RecalcularPago(pagoId)).Pago;

        var pagoData = actualizado.With(("nombre_completo", pago.S("nombre_completo")), ("depto_numero", pago.I("depto_numero")));

        var usuarioId = pago.S("inquilino_usuario_id");
        if (!string.IsNullOrEmpty(usuarioId))
        {
            SseService.EmitToUser(usuarioId, "payment_confirmed", new { pago = pagoData });
            Js.Bg(() => PushService.CreateAndSendNotification(usuarioId, "✅ ¡Pago confirmado!", $"Tu pago de {pago.S("periodo")} fue confirmado", "pago"));
        }
        return Api.Data(pagoData);
    }

    // ── GET /comprobantes-pendientes ─────────────────────────────────────────
    static async Task<IResult> ComprobantesPendientes(HttpContext c)
    {
        var u = c.Me();
        // Admin ve solo los suyos; cobrador ve todos (sin restricción de admin_id), igual que antes.
        var isAdmin = u.Rol == "admin";
        var r = await Db.Query(
            $@"SELECT p.id, p.monto, p.periodo, p.comprobante_url, p.comprobante_subido_en,
                      i.nombre_completo, i.depto_numero, i.id AS inquilino_id
               FROM pagos p
               JOIN inquilinos i ON i.id = p.inquilino_id
               WHERE p.comprobante_url IS NOT NULL
                 AND p.confirmado = false
                 AND p.rechazado = false
                 {(isAdmin ? "AND i.admin_id = $1" : "")}
               ORDER BY p.comprobante_subido_en DESC",
            isAdmin ? new object?[] { u.Id } : Array.Empty<object?>());
        return Api.Data(r.Rows);
    }

    // ── GET /historial/:inquilino_id ─────────────────────────────────────────
    static async Task<IResult> HistorialPagos(HttpContext c)
    {
        var inquilinoId = Api.Route(c, "inquilino_id");
        var u = c.Me();
        var authCheck = u.Rol == "inquilino" ? "AND usuario_id = $2" : "AND admin_id = $2";
        var own = u.Rol == "cobrador"
            ? await Db.Query("SELECT id FROM inquilinos WHERE id = $1", inquilinoId)
            : await Db.Query($"SELECT id FROM inquilinos WHERE id = $1 {authCheck}", inquilinoId, u.Id);
        if (own.First == null) throw new AppError("No autorizado", 403);

        var r = await Db.Query(
            @"SELECT p.*,
                     i.fecha_pago, i.nombre_completo, i.depto_numero,
                     u.nombre_completo AS escaneado_por_nombre,
                     COALESCE((SELECT c.valor FROM configuracion c
                               WHERE c.admin_id = i.admin_id AND c.clave = 'dias_gracia_retraso'), '0') AS dias_gracia
              FROM pagos p
              JOIN inquilinos i ON i.id = p.inquilino_id
              LEFT JOIN usuarios u ON u.id = p.escaneado_por
              WHERE p.inquilino_id = $1
              ORDER BY p.periodo DESC", inquilinoId);

        var pagos = r.Rows.Select(p =>
        {
            bool? aTiempo = null;
            if (p.B("confirmado") && !string.IsNullOrEmpty(p.S("confirmado_en")) && !string.IsNullOrEmpty(p.S("fecha_pago")))
            {
                var confirmado = p.Dt("confirmado_en")!.Value;
                var partes = p.S("periodo")!.Split('-');
                var anio = int.Parse(partes[0]); var mes = int.Parse(partes[1]);
                var diaPago = ReportesEndpoints.ParseInt(p.S("fecha_pago"));
                var gracia = ReportesEndpoints.ParseInt(p.S("dias_gracia")) ?? 0;
                if (diaPago == null) aTiempo = false; // new Date(NaN) → comparación falsa
                else
                {
                    var limite = new DateTime(anio, mes, 1, 23, 59, 59, DateTimeKind.Local).AddDays(diaPago.Value + gracia - 1);
                    aTiempo = confirmado <= limite.ToUniversalTime();
                }
            }
            return p.Without("dias_gracia").With(("a_tiempo", aTiempo));
        }).ToList();
        return Api.Data(pagos);
    }

    // ── POST /marcar-pagado/:inquilino_id ────────────────────────────────────
    static async Task<IResult> MarcarPagadoAdmin(HttpContext c)
    {
        var inquilinoId = Api.Route(c, "inquilino_id");
        var b = await Api.Body(c);
        var soloRenta = b["soloRenta"] is JsonValue sv && sv.TryGetValue<bool>(out var sb) && sb;
        var periodo = Js.CurrentPeriodo();
        var u = c.Me();

        var inqRes = await Db.Query("SELECT * FROM inquilinos WHERE id = $1 AND admin_id = $2 AND estado = 'activo'", inquilinoId, u.Id);
        var inquilino = inqRes.First ?? throw new AppError("Inquilino no encontrado o no autorizado", 404);

        var pagoRes = await Db.Query("SELECT * FROM pagos WHERE inquilino_id = $1 AND periodo = $2", inquilinoId, periodo);
        Row pago;
        if (pagoRes.First == null)
        {
            var montoTotal = inquilino.D("renta") + await TotalExtraPendiente(inquilinoId);
            pago = (await Db.Query("INSERT INTO pagos (inquilino_id, periodo, monto, metodo) VALUES ($1, $2, $3, $4) RETURNING *",
                inquilinoId, periodo, montoTotal, Metodo(inquilino))).First!;
        }
        else if (pagoRes.First.B("confirmado"))
            return Api.Obj(200, ("data", pagoRes.First), ("yaConfirmado", true));
        else pago = pagoRes.First;

        var pagoId = pago.S("id")!;
        await Db.Query("UPDATE pagos SET escaneado_por = $2, escaneado_en = NOW() WHERE id = $1", pagoId, u.Actor);

        var totalAbonado = await TotalAbonado(pagoId);
        var saldoPendiente = Math.Max(pago.D("monto") - totalAbonado, 0);

        // "Solo renta": cobra la renta pero no los cargos extra pendientes del periodo.
        var montoAAbonar = saldoPendiente;
        var nota = "Marcado como pagado completo por admin";
        if (soloRenta)
        {
            var rentaPendiente = Math.Max(inquilino.D("renta") - totalAbonado, 0);
            montoAAbonar = Math.Min(rentaPendiente, saldoPendiente);
            nota = "Marcado como pagado (solo renta, cargos extra pendientes) por admin";
        }

        if (montoAAbonar > 0)
            await Db.Query("INSERT INTO abonos_pago (pago_id, monto, metodo, nota, registrado_por) VALUES ($1, $2, $3, $4, $5)",
                pagoId, montoAAbonar, Metodo(inquilino), nota, u.Actor);
        var actualizado = (await SaldoService.RecalcularPago(pagoId)).Pago;

        var pagoData = actualizado.With(("nombre_completo", inquilino.S("nombre_completo")), ("depto_numero", inquilino.I("depto_numero")));

        var adminId = inquilino.S("admin_id");
        if (!string.IsNullOrEmpty(adminId))
            Js.Bg(() => PushService.NotificarEquipoPagos(adminId, "💵 Pago registrado",
                $"Depto {inquilino.S("depto_numero")} — {inquilino.S("nombre_completo")} · {periodo}"));
        var usuarioId = inquilino.S("usuario_id");
        if (!string.IsNullOrEmpty(usuarioId))
        {
            SseService.EmitToUser(usuarioId, "payment_confirmed", new { pago = pagoData });
            Js.Bg(() => PushService.CreateAndSendNotification(usuarioId, "✅ ¡Pago confirmado!",
                $"Tu pago de {periodo} fue confirmado por el administrador", "pago"));
        }
        return Api.Data(pagoData);
    }

    // ── DELETE /:pago_id/cancelar (solo dentro de 24 h) ──────────────────────
    static async Task<IResult> CancelarPago(HttpContext c)
    {
        var pagoId = Api.Route(c, "pago_id");
        var r = await Db.Query(
            @"SELECT p.id, p.inquilino_id, p.periodo, p.confirmado_en, p.created_at, i.usuario_id
              FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE p.id = $1 AND i.admin_id = $2", pagoId, c.Me().Id);
        var pago = r.First ?? throw new AppError("Pago no encontrado o no autorizado", 404);

        var refDate = (pago.Dt("confirmado_en") ?? pago.Dt("created_at"))!.Value;
        if ((DateTime.UtcNow - refDate).TotalHours > 24)
            throw new AppError("Ya pasó más de un día. Para corregir este pago usa un abono manual.", 400);

        // abonos_pago y promesas_pago se borran en cascada. Los cargos extra "pagados" por este pago vuelven a pendientes.
        await Db.Query(
            @"UPDATE cuotas_extra SET estado = 'pendiente', pagado_en = NULL
              WHERE inquilino_id = $1 AND periodo = $2 AND estado = 'pagado'", pago.S("inquilino_id"), pago.S("periodo"));
        await Db.Query("DELETE FROM pagos WHERE id = $1", pagoId);

        var usuarioId = pago.S("usuario_id");
        if (!string.IsNullOrEmpty(usuarioId))
        {
            SseService.EmitToUser(usuarioId, "payment_confirmed", new { pago = (object?)null });
            Js.Bg(() => PushService.CreateAndSendNotification(usuarioId, "Pago cancelado",
                $"El administrador canceló el registro de pago de {pago.S("periodo")}.", "pago"));
        }
        return Api.Msg("Pago cancelado");
    }

    // ── GET /estados-actuales ────────────────────────────────────────────────
    static async Task<IResult> EstadosActuales(HttpContext c)
    {
        var u = c.Me();
        var periodo = Js.CurrentPeriodo();
        var isAdmin = u.Rol == "admin";
        var r = await Db.Query(
            $@"SELECT i.id AS inquilino_id,
                      p.id AS pago_id,
                      p.monto,
                      COALESCE(p.confirmado, false) AS confirmado,
                      p.confirmado_en,
                      COALESCE(p.rechazado, false) AS rechazado,
                      p.comprobante_url,
                      COALESCE(ab.total_abonado, 0) AS total_abonado
               FROM inquilinos i
               LEFT JOIN pagos p ON p.inquilino_id = i.id AND p.periodo = $1
               LEFT JOIN LATERAL (
                 SELECT SUM(monto) AS total_abonado FROM abonos_pago WHERE pago_id = p.id
               ) ab ON true
               WHERE i.estado = 'activo' {(isAdmin ? "AND i.admin_id = $2" : "")}",
            isAdmin ? new object?[] { periodo, u.Id } : new object?[] { periodo });
        return Api.Data(r.Rows);
    }

    // ── GET /estado/:inquilino_id ────────────────────────────────────────────
    static async Task<IResult> EstadoPago(HttpContext c)
    {
        var inquilinoId = Api.Route(c, "inquilino_id");
        var periodo = Js.CurrentPeriodo();
        var u = c.Me();
        var authCheck = u.Rol == "inquilino" ? "AND usuario_id = $2" : "AND admin_id = $2";
        var own = await Db.Query($"SELECT id FROM inquilinos WHERE id = $1 {authCheck}", inquilinoId, u.Id);
        if (own.First == null) throw new AppError("No autorizado", 403);

        var pagoTask = Db.Query("SELECT * FROM pagos WHERE inquilino_id = $1 AND periodo = $2", inquilinoId, periodo);
        var cuotasTask = Db.Query(
            @"SELECT id, concepto, monto, estado, created_at, pagado_en FROM cuotas_extra
              WHERE inquilino_id = $1 AND (estado = 'pendiente' OR (estado = 'pagado' AND periodo = $2))
              ORDER BY created_at", inquilinoId, periodo);
        await Task.WhenAll(pagoTask, cuotasTask);

        var totalExtra = cuotasTask.Result.Rows.Where(x => x.S("estado") == "pendiente").Sum(x => (double)x.D("monto"));
        var pago = pagoTask.Result.First;
        double totalAbonado = 0;
        if (pago != null) totalAbonado = (double)await TotalAbonado(pago.S("id")!);

        return Api.Obj(200,
            ("data", pago),
            ("cuotas", cuotasTask.Result.Rows),
            ("total_extra", totalExtra),
            ("total_abonado", totalAbonado),
            ("saldo_pendiente", pago != null ? Math.Max((double)pago.D("monto") - totalAbonado, 0) : 0),
            ("periodo", periodo));
    }

    // ── POST /abono ──────────────────────────────────────────────────────────
    static async Task<IResult> RegistrarAbono(HttpContext c)
    {
        var b = await Api.Body(c);
        if (!Js.T(b["inquilino_id"])) throw new AppError("inquilino_id es requerido", 400);
        var montoAbono = Js.ParseFloat(b["monto"]);
        if (double.IsNaN(montoAbono) || montoAbono <= 0) throw new AppError("El monto del abono debe ser mayor a 0", 400);

        var u = c.Me();
        var inqRes = u.Rol == "admin"
            ? await Db.Query("SELECT * FROM inquilinos WHERE id = $1 AND admin_id = $2 AND estado = 'activo'", b["inquilino_id"], u.Id)
            : await Db.Query("SELECT * FROM inquilinos WHERE id = $1  AND estado = 'activo'", b["inquilino_id"]);
        var inquilino = inqRes.First ?? throw new AppError("Inquilino no encontrado o no autorizado", 404);

        var periodoFinal = Js.T(b["periodo"]) ? b.S("periodo")! : Js.CurrentPeriodo();
        var pago = await GetOrCrearPago(inquilino, periodoFinal);
        if (pago.B("confirmado")) throw new AppError("Este periodo ya está pagado por completo", 400);

        var metodo = Js.T(b["metodo"]) ? b.S("metodo") : Metodo(inquilino);
        var abonoRes = await Db.Query(
            @"INSERT INTO abonos_pago (pago_id, monto, fecha, metodo, nota, comprobante_url, registrado_por)
              VALUES ($1, $2, COALESCE($3, CURRENT_DATE), $4, $5, $6, $7) RETURNING *",
            pago.S("id"), montoAbono, Js.T(b["fecha"]) ? b.S("fecha") : null, metodo,
            Js.T(b["nota"]) ? b.S("nota") : null, Js.T(b["comprobante_url"]) ? b.S("comprobante_url") : null, u.Actor);

        var rec = await SaldoService.RecalcularPago(pago.S("id")!);

        var pagoData = rec.Pago.With(("nombre_completo", inquilino.S("nombre_completo")), ("depto_numero", inquilino.I("depto_numero")));
        var usuarioId = inquilino.S("usuario_id");
        if (!string.IsNullOrEmpty(usuarioId))
        {
            if (rec.Completo)
            {
                SseService.EmitToUser(usuarioId, "payment_confirmed", new { pago = pagoData });
                Js.Bg(() => PushService.CreateAndSendNotification(usuarioId, "✅ ¡Pago confirmado!", $"Tu pago de {periodoFinal} fue confirmado", "pago"));
            }
            else
            {
                Js.Bg(() => PushService.CreateAndSendNotification(usuarioId, "💰 Abono registrado",
                    $"Se registró un abono de ${Js.NumMx(montoAbono)} a tu renta de {periodoFinal}. Saldo pendiente: ${Js.NumMx((double)rec.SaldoPendiente)}", "pago"));
            }
        }

        return Api.Data(new Dictionary<string, object?>
        {
            ["abono"] = abonoRes.First, ["pago"] = rec.Pago,
            ["totalAbonado"] = (double)rec.TotalAbonado, ["saldoPendiente"] = (double)rec.SaldoPendiente,
        });
    }

    // ── GET /abonos/:pago_id ─────────────────────────────────────────────────
    static async Task<IResult> AbonosPago(HttpContext c)
    {
        var pagoId = Api.Route(c, "pago_id");
        var u = c.Me();
        var authCheck = u.Rol == "inquilino" ? "AND i.usuario_id = $2" : u.Rol == "admin" ? "AND i.admin_id = $2" : "";
        var own = u.Rol == "cobrador"
            ? await Db.Query($"SELECT p.id FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id WHERE p.id = $1 {authCheck}", pagoId)
            : await Db.Query($"SELECT p.id FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id WHERE p.id = $1 {authCheck}", pagoId, u.Id);
        if (own.First == null) throw new AppError("No autorizado", 403);

        var r = await Db.Query(
            @"SELECT a.*, u.nombre_completo AS registrado_por_nombre
              FROM abonos_pago a
              LEFT JOIN usuarios u ON u.id = a.registrado_por
              WHERE a.pago_id = $1
              ORDER BY a.fecha ASC, a.created_at ASC", pagoId);
        return Api.Data(r.Rows);
    }

    // ── PUT /abono/:abono_id ─────────────────────────────────────────────────
    static async Task<IResult> EditarAbono(HttpContext c)
    {
        var abonoId = Api.Route(c, "abono_id");
        var b = await Api.Body(c);

        var abonoRes = await Db.Query(
            @"SELECT a.*, p.id AS pago_id
              FROM abonos_pago a
              JOIN pagos p ON p.id = a.pago_id
              JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE a.id = $1 AND i.admin_id = $2", abonoId, c.Me().Id);
        var abono = abonoRes.First ?? throw new AppError("Abono no encontrado o no autorizado", 404);

        if (b.ContainsKey("monto"))
        {
            var m = Js.ParseFloat(b["monto"]);
            if (double.IsNaN(m) || m == 0 || m <= 0) throw new AppError("El monto del abono debe ser mayor a 0", 400);
        }

        var updated = await Db.Query(
            @"UPDATE abonos_pago SET
                monto  = COALESCE($2, monto),
                fecha  = COALESCE($3, fecha),
                metodo = COALESCE($4, metodo),
                nota   = COALESCE($5, nota)
              WHERE id = $1 RETURNING *", abonoId, b["monto"], b["fecha"], b["metodo"], b["nota"]);

        var rec = await SaldoService.RecalcularPago(abono.S("pago_id")!);
        return Api.Data(new Dictionary<string, object?>
        {
            ["abono"] = updated.First, ["pago"] = rec.Pago,
            ["totalAbonado"] = (double)rec.TotalAbonado, ["saldoPendiente"] = (double)rec.SaldoPendiente,
        });
    }

    // ── DELETE /abono/:abono_id ──────────────────────────────────────────────
    static async Task<IResult> EliminarAbono(HttpContext c)
    {
        var abonoId = Api.Route(c, "abono_id");
        var abonoRes = await Db.Query(
            @"SELECT a.pago_id
              FROM abonos_pago a
              JOIN pagos p ON p.id = a.pago_id
              JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE a.id = $1 AND i.admin_id = $2", abonoId, c.Me().Id);
        var abono = abonoRes.First ?? throw new AppError("Abono no encontrado o no autorizado", 404);

        await Db.Query("DELETE FROM abonos_pago WHERE id = $1", abonoId);
        var rec = await SaldoService.RecalcularPago(abono.S("pago_id")!);
        return Api.Data(new Dictionary<string, object?>
        {
            ["pago"] = rec.Pago, ["totalAbonado"] = (double)rec.TotalAbonado, ["saldoPendiente"] = (double)rec.SaldoPendiente,
        });
    }

    // ── GET /saldo/:inquilino_id ─────────────────────────────────────────────
    static async Task<IResult> SaldoInquilino(HttpContext c)
    {
        var inquilinoId = Api.Route(c, "inquilino_id");
        var u = c.Me();
        var authCheck = u.Rol == "inquilino" ? "AND usuario_id = $2" : u.Rol == "admin" ? "AND admin_id = $2" : "";
        var inqRes = u.Rol == "cobrador"
            ? await Db.Query($"SELECT id, renta, fecha_inicio::text AS fecha_inicio_txt FROM inquilinos WHERE id = $1 {authCheck}", inquilinoId)
            : await Db.Query($"SELECT id, renta, fecha_inicio::text AS fecha_inicio_txt FROM inquilinos WHERE id = $1 {authCheck}", inquilinoId, u.Id);
        var inq = inqRes.First ?? throw new AppError("No autorizado", 403);

        var detalle = await Db.Query(
            @"WITH periodos AS (
                SELECT to_char(gs, 'YYYY-MM') AS periodo
                FROM generate_series(
                  date_trunc('month', $2::date), date_trunc('month', CURRENT_DATE), interval '1 month'
                ) AS gs
              ),
              cuotas AS (
                SELECT periodo, SUM(monto) AS total FROM cuotas_extra WHERE inquilino_id = $1 GROUP BY periodo
              ),
              abonado AS (
                SELECT p.periodo, COALESCE(SUM(a.monto), 0) AS total
                FROM pagos p LEFT JOIN abonos_pago a ON a.pago_id = p.id
                WHERE p.inquilino_id = $1 GROUP BY p.periodo
              )
              SELECT pe.periodo,
                     ($3::numeric + COALESCE(c.total, 0)) AS monto_esperado,
                     COALESCE(ab.total, 0) AS abonado,
                     GREATEST($3::numeric + COALESCE(c.total, 0) - COALESCE(ab.total, 0), 0) AS saldo
              FROM periodos pe
              LEFT JOIN cuotas c ON c.periodo = pe.periodo
              LEFT JOIN abonado ab ON ab.periodo = pe.periodo
              ORDER BY pe.periodo", inquilinoId, inq.S("fecha_inicio_txt"), inq.S("renta"));

        var deudaTotal = detalle.Rows.Sum(r => (double)r.D("saldo"));
        return Api.Data(new Dictionary<string, object?> { ["deuda_total"] = deudaTotal, ["detalle"] = detalle.Rows });
    }

    // ── Promesas de pago ─────────────────────────────────────────────────────
    static string? NormalizarFechaPromesa(JsonNode? raw)
    {
        if (raw is null) return null;
        var s = Js.Str(raw)?.Trim();
        if (string.IsNullOrEmpty(s)) return null;

        var dmy = Regex.Match(s, @"^(\d{1,2})[/\-](\d{1,2})[/\-](\d{4})$");
        if (dmy.Success) s = $"{dmy.Groups[3].Value}-{dmy.Groups[2].Value.PadLeft(2, '0')}-{dmy.Groups[1].Value.PadLeft(2, '0')}";

        if (!Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}$"))
            throw new AppError("La fecha debe tener el formato DD/MM/AAAA o AAAA-MM-DD", 400);
        return s;
    }

    static async Task<Row?> AplicarPromesa(string pagoId, string? fecha, string userId)
    {
        var updated = await Db.Query("UPDATE pagos SET fecha_promesa = $2 WHERE id = $1 RETURNING *", pagoId, fecha);
        // Cada vez que se guarda una fecha (no al borrarla) se agrega al historial.
        if (fecha != null)
            await Db.Query("INSERT INTO promesas_pago (pago_id, fecha_promesa, creado_por) VALUES ($1, $2, $3)", pagoId, fecha, userId);
        return updated.First;
    }

    static async Task<IResult> SetPromesa(HttpContext c)
    {
        var pagoId = Api.Route(c, "pago_id");
        var b = await Api.Body(c);
        var fecha = NormalizarFechaPromesa(b["fecha_promesa"]);

        var pagoRes = await Db.Query(
            "SELECT p.id FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id WHERE p.id = $1 AND i.admin_id = $2",
            pagoId, c.Me().Id);
        if (pagoRes.First == null) throw new AppError("Pago no encontrado o no autorizado", 404);

        return Api.Data(await AplicarPromesa(pagoId, fecha, c.Me().Actor));
    }

    static async Task<IResult> SetPromesaInquilino(HttpContext c)
    {
        var inquilinoId = Api.Route(c, "inquilino_id");
        var b = await Api.Body(c);
        var fecha = NormalizarFechaPromesa(b["fecha_promesa"]);
        var per = Js.T(b["periodo"]) ? b.S("periodo")! : null;
        var periodo = per != null && Regex.IsMatch(per, @"^\d{4}-\d{2}$") ? per : Js.CurrentPeriodo();
        var u = c.Me();

        var inqRes = await Db.Query("SELECT * FROM inquilinos WHERE id = $1 AND admin_id = $2 AND estado = 'activo'", inquilinoId, u.Id);
        if (inqRes.First == null) throw new AppError("Inquilino no encontrado o no autorizado", 404);

        var existente = await Db.Query("SELECT * FROM pagos WHERE inquilino_id = $1 AND periodo = $2", inquilinoId, periodo);

        // Borrar la promesa cuando ni siquiera hay pago: no hay nada que guardar.
        if (fecha == null && existente.First == null) return Api.Data(null);

        var pago = existente.First ?? await GetOrCrearPago(inqRes.First, periodo);
        if (pago.B("confirmado")) throw new AppError("Este periodo ya está pagado por completo", 400);

        return Api.Data(await AplicarPromesa(pago.S("id")!, fecha, u.Actor));
    }

    static async Task<IResult> PromesasHistorial(HttpContext c)
    {
        var inquilinoId = Api.Route(c, "inquilino_id");
        var u = c.Me();
        var whereAdmin = u.Rol == "admin" ? "AND i.admin_id = $2" : "AND i.usuario_id = $2";
        var r = await Db.Query(
            $@"SELECT pp.id, pp.fecha_promesa, pp.created_at,
                      p.periodo, p.monto, p.confirmado, p.confirmado_en
               FROM promesas_pago pp
               JOIN pagos p ON p.id = pp.pago_id
               JOIN inquilinos i ON i.id = p.inquilino_id
               WHERE p.inquilino_id = $1 {whereAdmin}
               ORDER BY pp.created_at DESC", inquilinoId, u.Id);

        var data = r.Rows.Select(row =>
        {
            var estado = "pendiente";
            if (row.B("confirmado") && !string.IsNullOrEmpty(row.S("confirmado_en")))
            {
                var pagado = row.S("confirmado_en")![..10];
                var prometida = row.S("fecha_promesa")![..10];
                estado = string.CompareOrdinal(pagado, prometida) <= 0 ? "cumplida" : "incumplida";
            }
            return row.With(("estado_cumplimiento", estado));
        }).ToList();
        return Api.Data(data);
    }

    static async Task<IResult> EliminarPromesa(HttpContext c)
    {
        var r = await Db.Query(
            @"DELETE FROM promesas_pago pp
              USING pagos p, inquilinos i
              WHERE pp.id = $1 AND pp.pago_id = p.id AND p.inquilino_id = i.id AND i.admin_id = $2
              RETURNING pp.id", Api.Route(c, "id"), c.Me().Id);
        if (r.First == null) throw new AppError("Registro no encontrado o no autorizado", 404);
        return Api.Ok();
    }

    // ── Deuda (saldos) ───────────────────────────────────────────────────────
    // Fecha límite del mes `gs`: primer día + (día pactado - 1) + días de gracia. Un mes solo cuenta
    // como DEUDA VENCIDA cuando CURRENT_DATE ya pasó esa fecha.
    const string MesVencido = @"CURRENT_DATE > (
  gs::date
  + (COALESCE(NULLIF(regexp_replace(i.fecha_pago, '[^0-9]', '', 'g'), '')::int, 1) - 1)
  + COALESCE((SELECT c.valor FROM configuracion c
              WHERE c.admin_id = i.admin_id AND c.clave = 'dias_gracia_retraso')::int, 0)
)";
    const string DeudaMes = "i.renta + COALESCE(cx.total, 0) - COALESCE(ab.total, 0)";
    const string DeudaVencida = "COALESCE(SUM(GREATEST(CASE WHEN " + MesVencido + " THEN " + DeudaMes + " ELSE 0 END, 0)), 0)";
    const string DeudaTotal = "COALESCE(SUM(GREATEST(" + DeudaMes + ", 0)), 0)";
    const string PeriodoVencido = "MIN(CASE WHEN " + MesVencido + " AND " + DeudaMes + " > 0.5 THEN to_char(gs, 'YYYY-MM') END)";

    const string SaldosFrom = @"
FROM inquilinos i
CROSS JOIN LATERAL generate_series(
  date_trunc('month', i.fecha_inicio), date_trunc('month', CURRENT_DATE), interval '1 month'
) AS gs
LEFT JOIN LATERAL (
  SELECT SUM(monto) AS total FROM cuotas_extra
  WHERE inquilino_id = i.id AND periodo = to_char(gs, 'YYYY-MM')
) cx ON true
LEFT JOIN LATERAL (
  SELECT SUM(a.monto) AS total FROM pagos p JOIN abonos_pago a ON a.pago_id = p.id
  WHERE p.inquilino_id = i.id AND p.periodo = to_char(gs, 'YYYY-MM')
) ab ON true";

    const string ExtraCols = @"COALESCE((SELECT SUM(monto) FROM cuotas_extra WHERE inquilino_id = i.id AND estado = 'pendiente'), 0) AS deuda_cuotas_extra,
GREATEST(MAX(i.deposito) - COALESCE((SELECT SUM(monto) FROM abonos_deposito WHERE inquilino_id = i.id), 0), 0) AS deuda_deposito";

    static async Task<IResult> SaldosInquilinos(HttpContext c)
    {
        var u = c.Me();
        var isAdmin = u.Rol == "admin";
        var r = await Db.Query(
            $@"SELECT i.id AS inquilino_id, i.depto_numero, i.nombre_completo,
                      {DeudaVencida} AS deuda_vencida,
                      {DeudaTotal} AS deuda_total,
                      {PeriodoVencido} AS periodo_vencido,
                      {ExtraCols}
               {SaldosFrom}
               WHERE i.estado = 'activo' {(isAdmin ? "AND i.admin_id = $1" : "")}
               GROUP BY i.id",
            isAdmin ? new object?[] { u.Id } : Array.Empty<object?>());
        return Api.Data(r.Rows);
    }

    static async Task<IResult> ResumenDeuda(HttpContext c)
    {
        var u = c.Me();
        var isAdmin = u.Rol == "admin";
        var r = await Db.Query(
            $@"SELECT * FROM (
                 SELECT i.id AS inquilino_id, i.depto_numero, i.nombre_completo,
                        {DeudaVencida} AS deuda_vencida,
                        {DeudaTotal} AS deuda_total,
                        {ExtraCols}
                 {SaldosFrom}
                 WHERE i.estado = 'activo' {(isAdmin ? "AND i.admin_id = $1" : "")}
                 GROUP BY i.id
               ) sub",
            isAdmin ? new object?[] { u.Id } : Array.Empty<object?>());

        // por_departamento y total_general = solo atraso real; total_por_cobrar = todo lo pendiente del ciclo.
        var porDepto = r.Rows.Where(x => (double)x.D("deuda_vencida") > 0).ToList();
        var totalGeneral = r.Rows.Sum(x => (double)x.D("deuda_vencida"));
        var totalPorCobrar = r.Rows.Sum(x => (double)x.D("deuda_total"));
        return Api.Data(new Dictionary<string, object?>
        {
            ["total_general"] = totalGeneral, ["total_por_cobrar"] = totalPorCobrar, ["por_departamento"] = porDepto,
        });
    }
}
