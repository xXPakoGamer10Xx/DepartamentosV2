namespace VertexRent.Api;

public static class CuotasEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/cuotas").Auth().AdminOnly();
        g.MapGet("/", H(GetCuotas));
        g.MapPost("/", H(CreateCuota));
        g.MapPut("/{id}/pagar", H(PagarCuota));
        g.MapDelete("/{id}", H(DeleteCuota));
    }

    static async Task<IResult> GetCuotas(HttpContext c)
    {
        var where = "WHERE i.admin_id = $1";
        var ps = new List<object?> { c.Me().Id };
        var idx = 2;
        var inq = Api.Query(c, "inquilino_id"); var estado = Api.Query(c, "estado");
        if (inq != null) { where += $" AND c.inquilino_id = ${idx++}"; ps.Add(inq); }
        if (estado != null) { where += $" AND c.estado = ${idx++}"; ps.Add(estado); }

        var r = await Db.Query(
            $@"SELECT c.*, u.nombre_completo as creado_por_nombre
               FROM cuotas_extra c
               JOIN inquilinos i ON i.id = c.inquilino_id
               LEFT JOIN usuarios u ON u.id = c.creado_por
               {where}
               ORDER BY c.created_at DESC", ps.ToArray());
        return Api.Data(r.Rows);
    }

    static async Task<IResult> CreateCuota(HttpContext c)
    {
        var b = await Api.Body(c);
        var concepto = b.S("concepto")?.Trim();
        if (!Js.T(b["inquilino_id"]) || string.IsNullOrEmpty(concepto) || !Js.T(b["monto"]))
            throw new AppError("inquilino_id, concepto y monto son requeridos", 400);
        var monto = Js.ParseFloat(b["monto"]);
        if (double.IsNaN(monto) || monto <= 0) throw new AppError("El monto debe ser mayor a cero", 400);

        var u = c.Me();
        var inqCheck = await Db.Query("SELECT id, usuario_id, depto_numero FROM inquilinos WHERE id = $1 AND admin_id = $2", b["inquilino_id"], u.Id);
        var inquilino = inqCheck.First ?? throw new AppError("Inquilino no encontrado o no autorizado", 403);

        var periodo = Js.CurrentPeriodo();
        var r = await Db.Query(
            "INSERT INTO cuotas_extra (inquilino_id, concepto, monto, periodo, creado_por) VALUES ($1, $2, $3, $4, $5) RETURNING *",
            b["inquilino_id"], concepto, monto, periodo, u.Actor);

        // Sumar el cargo al pago del periodo (si existe sin confirmar) para mantener el total consistente.
        var pagoRes = await Db.Query(
            "UPDATE pagos SET monto = monto + $1 WHERE inquilino_id = $2 AND periodo = $3 AND confirmado = false RETURNING id",
            monto, b["inquilino_id"], periodo);
        if (pagoRes.First != null) await SaldoService.RecalcularPago(pagoRes.First.S("id")!);

        var usuarioId = inquilino.S("usuario_id");
        if (!string.IsNullOrEmpty(usuarioId))
            await PushService.CreateAndSendNotification(usuarioId, "🔔 Nuevo cargo adicional",
                $"Se ha generado un cobro extra de {Js.MXN((decimal)monto)} por: {concepto}", "cuota");

        return Api.Data(r.First, 201);
    }

    static async Task<IResult> DeleteCuota(HttpContext c)
    {
        var r = await Db.Query(
            @"DELETE FROM cuotas_extra c
              USING inquilinos i
              WHERE c.id = $1 AND c.inquilino_id = i.id AND i.admin_id = $2 AND c.estado = 'pendiente'
              RETURNING c.id, c.inquilino_id, c.periodo, c.monto", Api.Route(c, "id"), c.Me().Id);
        var cuota = r.First ?? throw new AppError("Cuota no encontrada o ya fue pagada", 404);

        var pagoRes = await Db.Query(
            "UPDATE pagos SET monto = GREATEST(monto - $1, 0) WHERE inquilino_id = $2 AND periodo = $3 AND confirmado = false RETURNING id",
            cuota.S("monto"), cuota.S("inquilino_id"), cuota.S("periodo"));
        if (pagoRes.First != null) await SaldoService.RecalcularPago(pagoRes.First.S("id")!);

        return Api.Ok();
    }

    // Marca un cargo extra como pagado registrando un abono por su monto en el pago de SU periodo.
    static async Task<IResult> PagarCuota(HttpContext c)
    {
        var u = c.Me();
        var cuotaRes = await Db.Query(
            @"SELECT c.* FROM cuotas_extra c
              JOIN inquilinos i ON i.id = c.inquilino_id
              WHERE c.id = $1 AND i.admin_id = $2 AND c.estado = 'pendiente'", Api.Route(c, "id"), u.Id);
        var cuota = cuotaRes.First ?? throw new AppError("Cargo no encontrado o ya fue pagado", 404);

        var inquilino = (await Db.Query("SELECT * FROM inquilinos WHERE id = $1", cuota.S("inquilino_id"))).First!;

        var pagoSel = await Db.Query("SELECT * FROM pagos WHERE inquilino_id = $1 AND periodo = $2", cuota.S("inquilino_id"), cuota.S("periodo"));
        var pago = pagoSel.First ?? await PagosEndpoints.GetOrCrearPago(inquilino, cuota.S("periodo")!);

        var metodo = string.IsNullOrEmpty(inquilino.S("metodo_pago")) ? "efectivo" : inquilino.S("metodo_pago");
        await Db.Query("INSERT INTO abonos_pago (pago_id, monto, metodo, nota, registrado_por) VALUES ($1, $2, $3, $4, $5)",
            pago.S("id"), cuota.S("monto"), metodo, $"Cargo pagado: {cuota.S("concepto")}", u.Id);
        await Db.Query("UPDATE cuotas_extra SET estado = 'pagado', pagado_en = NOW() WHERE id = $1", cuota.S("id"));

        var rec = await SaldoService.RecalcularPago(pago.S("id")!);
        return Api.Data(new Dictionary<string, object?>
        {
            ["pago"] = rec.Pago, ["totalAbonado"] = (double)rec.TotalAbonado,
            ["completo"] = rec.Completo, ["saldoPendiente"] = (double)rec.SaldoPendiente,
        });
    }
}
