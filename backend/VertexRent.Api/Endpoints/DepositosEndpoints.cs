namespace VertexRent.Api;

public static class DepositosEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/depositos").Auth();

        g.MapPost("/abono", H(RegistrarAbono)).CobradorOrAdmin();
        g.MapPut("/abono/{abono_id}", H(EditarAbono)).AdminOnly();
        g.MapDelete("/abono/{abono_id}", H(EliminarAbono)).AdminOnly();
        g.MapGet("/{inquilino_id}/saldo", H(Saldo));
        g.MapGet("/{inquilino_id}/abonos", H(Abonos));
    }

    static async Task<Row?> CheckOwnership(HttpContext c, string select, string inquilinoId)
    {
        var u = c.Me();
        var authCheck = u.Rol == "inquilino" ? "AND usuario_id = $2" : u.Rol == "admin" ? "AND admin_id = $2" : "";
        var r = u.Rol == "cobrador"
            ? await Db.Query($"SELECT {select} FROM inquilinos WHERE id = $1 {authCheck}", inquilinoId)
            : await Db.Query($"SELECT {select} FROM inquilinos WHERE id = $1 {authCheck}", inquilinoId, u.Id);
        return r.First;
    }

    static async Task<IResult> Saldo(HttpContext c)
    {
        var id = Api.Route(c, "inquilino_id");
        var inq = await CheckOwnership(c, "id, deposito", id) ?? throw new AppError("No autorizado", 403);
        var pagado = (await Db.Query("SELECT COALESCE(SUM(monto), 0) AS pagado FROM abonos_deposito WHERE inquilino_id = $1", id)).First!.D("pagado");
        var total = inq.D("deposito");
        return Api.Data(new Dictionary<string, object?>
        {
            ["deposito_total"] = (double)total, ["deposito_pagado"] = (double)pagado,
            ["deposito_saldo"] = (double)Math.Max(total - pagado, 0),
        });
    }

    static async Task<IResult> Abonos(HttpContext c)
    {
        var id = Api.Route(c, "inquilino_id");
        _ = await CheckOwnership(c, "id", id) ?? throw new AppError("No autorizado", 403);
        var r = await Db.Query(
            @"SELECT a.*, u.nombre_completo AS registrado_por_nombre
              FROM abonos_deposito a
              LEFT JOIN usuarios u ON u.id = a.registrado_por
              WHERE a.inquilino_id = $1
              ORDER BY a.fecha ASC, a.created_at ASC", id);
        return Api.Data(r.Rows);
    }

    static async Task<IResult> RegistrarAbono(HttpContext c)
    {
        var b = await Api.Body(c);
        if (!Js.T(b["inquilino_id"])) throw new AppError("inquilino_id es requerido", 400);
        var monto = Js.ParseFloat(b["monto"]);
        if (double.IsNaN(monto) || monto <= 0) throw new AppError("El monto del abono debe ser mayor a 0", 400);

        var u = c.Me();
        var inqRes = u.Rol == "admin"
            ? await Db.Query("SELECT * FROM inquilinos WHERE id = $1 AND admin_id = $2 AND estado = 'activo'", b["inquilino_id"], u.Id)
            : await Db.Query("SELECT * FROM inquilinos WHERE id = $1  AND estado = 'activo'", b["inquilino_id"]);
        var inquilino = inqRes.First ?? throw new AppError("Inquilino no encontrado o no autorizado", 404);

        var metodo = Js.T(b["metodo"]) ? b.S("metodo") : (string.IsNullOrEmpty(inquilino.S("metodo_pago")) ? "efectivo" : inquilino.S("metodo_pago"));
        var abono = await Db.Query(
            @"INSERT INTO abonos_deposito (inquilino_id, monto, fecha, metodo, nota, comprobante_url, registrado_por)
              VALUES ($1, $2, COALESCE($3, CURRENT_DATE), $4, $5, $6, $7) RETURNING *",
            b["inquilino_id"], monto, Js.T(b["fecha"]) ? b.S("fecha") : null, metodo,
            Js.T(b["nota"]) ? b.S("nota") : null, Js.T(b["comprobante_url"]) ? b.S("comprobante_url") : null, u.Actor);

        var pagado = (await Db.Query("SELECT COALESCE(SUM(monto), 0) AS pagado FROM abonos_deposito WHERE inquilino_id = $1", b["inquilino_id"])).First!.D("pagado");
        var saldo = Math.Max(inquilino.D("deposito") - pagado, 0);

        var usuarioId = inquilino.S("usuario_id");
        if (!string.IsNullOrEmpty(usuarioId))
            Js.Bg(() => PushService.CreateAndSendNotification(usuarioId, "💰 Abono a depósito registrado",
                $"Se registró un abono de ${Js.NumMx(monto)} a tu depósito. Saldo pendiente: ${Js.NumMx((double)saldo)}", "pago"));

        return Api.Data(new Dictionary<string, object?>
        {
            ["abono"] = abono.First, ["deposito_pagado"] = (double)pagado, ["deposito_saldo"] = (double)saldo,
        });
    }

    static async Task<IResult> EditarAbono(HttpContext c)
    {
        var id = Api.Route(c, "abono_id");
        var b = await Api.Body(c);
        var found = await Db.Query(
            @"SELECT a.* FROM abonos_deposito a
              JOIN inquilinos i ON i.id = a.inquilino_id
              WHERE a.id = $1 AND i.admin_id = $2", id, c.Me().Id);
        if (found.First == null) throw new AppError("Abono no encontrado o no autorizado", 404);

        if (b.ContainsKey("monto"))
        {
            var m = Js.ParseFloat(b["monto"]);
            if (double.IsNaN(m) || m <= 0) throw new AppError("El monto del abono debe ser mayor a 0", 400);
        }

        var updated = await Db.Query(
            @"UPDATE abonos_deposito SET
                monto  = COALESCE($2, monto),
                fecha  = COALESCE($3, fecha),
                metodo = COALESCE($4, metodo),
                nota   = COALESCE($5, nota)
              WHERE id = $1 RETURNING *", id, b["monto"], b["fecha"], b["metodo"], b["nota"]);
        return Api.Data(new Dictionary<string, object?> { ["abono"] = updated.First });
    }

    static async Task<IResult> EliminarAbono(HttpContext c)
    {
        var id = Api.Route(c, "abono_id");
        var found = await Db.Query(
            @"SELECT a.id FROM abonos_deposito a
              JOIN inquilinos i ON i.id = a.inquilino_id
              WHERE a.id = $1 AND i.admin_id = $2", id, c.Me().Id);
        if (found.First == null) throw new AppError("Abono no encontrado o no autorizado", 404);
        await Db.Query("DELETE FROM abonos_deposito WHERE id = $1", id);
        return Api.Ok();
    }
}
