using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace VertexRent.Api;

public static class CuentasBancariasEndpoints
{
    static readonly Regex Clabe = new(@"^\d{18}$", RegexOptions.Compiled);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/cuentas-bancarias").Auth();

        // Ver: admin o colaborador con permiso 'cuentas'. Crear/editar/borrar: solo admin.
        g.MapGet("/", H(GetAll)).Permiso("cuentas");
        g.MapPost("/", H(Create)).AdminOnly();
        g.MapPut("/{id}", H(Update)).AdminOnly();
        g.MapPatch("/{id}/predeterminada", H(SetDefault)).AdminOnly();
        g.MapDelete("/{id}", H(Delete)).AdminOnly();
        g.MapPatch("/departamento/{numero}", H(AsignarDepto)).AdminOnly();
    }

    static string? TrimOrNull(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && s.Trim().Length > 0 ? s.Trim() : null;

    static async Task<IResult> GetAll(HttpContext c)
    {
        var r = await Db.Query(
            @"SELECT cb.*,
                (SELECT json_agg(d.numero ORDER BY d.numero)
                   FROM departamentos d WHERE d.cuenta_bancaria_id = cb.id) AS departamentos,
                (SELECT COALESCE(SUM(i.renta), 0)
                   FROM departamentos d
                   JOIN inquilinos i ON i.depto_numero = d.numero AND i.estado = 'activo'
                   WHERE d.cuenta_bancaria_id = cb.id) AS renta_mensual
              FROM cuentas_bancarias cb
              WHERE cb.admin_id = $1
              ORDER BY cb.es_predeterminada DESC, cb.created_at ASC", c.Me().OwnerId);
        return Api.Data(r.Rows);
    }

    static async Task<IResult> Create(HttpContext c)
    {
        var b = await Api.Body(c);
        var v = new Validator(b)
            .Match("banco_clabe", @"^\d{18}$", "La CLABE debe tener 18 dígitos")
            .Str("banco_nombre", max: 60, optional: true, trim: true)
            .Str("banco_titular", max: 120, optional: true, trim: true)
            .Str("alias", max: 60, optional: true, trim: true);
        if (v.Result is { } bad) return bad;

        var adminId = c.Me().Id;
        var clabe = b.S("banco_clabe");
        if (string.IsNullOrEmpty(clabe) || !Clabe.IsMatch(clabe))
            throw new AppError("La CLABE interbancaria debe tener exactamente 18 dígitos", 400);

        var count = await Db.Query("SELECT COUNT(*)::int AS n FROM cuentas_bancarias WHERE admin_id = $1", adminId);
        var esPrimera = count.First!.I("n") == 0;

        var r = await Db.Query(
            @"INSERT INTO cuentas_bancarias (admin_id, alias, banco_nombre, banco_clabe, banco_titular, es_predeterminada)
              VALUES ($1, $2, $3, $4, $5, $6) RETURNING *",
            adminId, TrimOrNull(b["alias"]), TrimOrNull(b["banco_nombre"]), clabe, TrimOrNull(b["banco_titular"]), esPrimera);
        return Api.Data(r.First, 201);
    }

    static async Task<IResult> Update(HttpContext c)
    {
        var adminId = c.Me().Id;
        var id = Api.Route(c, "id");
        var b = await Api.Body(c);

        var cur = await Db.Query("SELECT * FROM cuentas_bancarias WHERE id = $1 AND admin_id = $2", id, adminId);
        var cu = cur.First ?? throw new AppError("Cuenta no encontrada", 404);

        if (b.ContainsKey("banco_clabe") && !(b.S("banco_clabe") is { } cl && Clabe.IsMatch(cl) && b["banco_clabe"] is JsonValue))
            throw new AppError("La CLABE interbancaria debe tener exactamente 18 dígitos", 400);

        var r = await Db.Query(
            @"UPDATE cuentas_bancarias SET
                alias = $1, banco_nombre = $2, banco_clabe = $3, banco_titular = $4, activo = $5, updated_at = NOW()
              WHERE id = $6 AND admin_id = $7 RETURNING *",
            b.ContainsKey("alias") ? TrimOrNull(b["alias"]) : cu.S("alias"),
            b.ContainsKey("banco_nombre") ? TrimOrNull(b["banco_nombre"]) : cu.S("banco_nombre"),
            b["banco_clabe"] is null ? cu.S("banco_clabe") : b.S("banco_clabe"),
            b.ContainsKey("banco_titular") ? TrimOrNull(b["banco_titular"]) : cu.S("banco_titular"),
            b.ContainsKey("activo") ? Js.T(b["activo"]) : cu.B("activo"),
            id, adminId);
        return Api.Data(r.First);
    }

    static async Task<IResult> SetDefault(HttpContext c)
    {
        var adminId = c.Me().Id;
        var id = Api.Route(c, "id");
        var data = await Db.Tx(async tx =>
        {
            var owned = await tx.Query("SELECT id FROM cuentas_bancarias WHERE id = $1 AND admin_id = $2", id, adminId);
            if (owned.First == null) throw new AppError("Cuenta no encontrada", 404);
            await tx.Query("UPDATE cuentas_bancarias SET es_predeterminada = FALSE WHERE admin_id = $1", adminId);
            var r = await tx.Query(
                @"UPDATE cuentas_bancarias SET es_predeterminada = TRUE, activo = TRUE, updated_at = NOW()
                  WHERE id = $1 AND admin_id = $2 RETURNING *", id, adminId);
            return r.First;
        });
        return Api.Data(data);
    }

    static async Task<IResult> Delete(HttpContext c)
    {
        var adminId = c.Me().Id;
        var id = Api.Route(c, "id");

        var cur = await Db.Query("SELECT * FROM cuentas_bancarias WHERE id = $1 AND admin_id = $2", id, adminId);
        if (cur.First == null) throw new AppError("Cuenta no encontrada", 404);

        // Los departamentos que la usaban quedan en NULL por ON DELETE SET NULL.
        await Db.Query("DELETE FROM cuentas_bancarias WHERE id = $1 AND admin_id = $2", id, adminId);

        // Si era la predeterminada, promover otra cuenta activa.
        if (cur.First.B("es_predeterminada"))
            await Db.Query(
                @"UPDATE cuentas_bancarias SET es_predeterminada = TRUE
                  WHERE id = (SELECT id FROM cuentas_bancarias WHERE admin_id = $1 AND activo = TRUE
                              ORDER BY created_at ASC LIMIT 1)", adminId);

        // Sin ninguna cuenta: los inquilinos en transferencia/ambos pasan a efectivo.
        var restantes = await Db.Query("SELECT COUNT(*)::int AS n FROM cuentas_bancarias WHERE admin_id = $1", adminId);
        var actualizados = 0;
        if (restantes.First!.I("n") == 0)
        {
            var upd = await Db.Query(
                @"UPDATE inquilinos SET metodo_pago = 'efectivo', updated_at = NOW()
                  WHERE admin_id = $1 AND metodo_pago IN ('transferencia', 'ambos')", adminId);
            actualizados = upd.RowCount;
        }

        return Api.Obj(200, ("message", "Cuenta eliminada"), ("inquilinos_a_efectivo", actualizados));
    }

    static async Task<IResult> AsignarDepto(HttpContext c)
    {
        var adminId = c.Me().Id;
        var numero = Api.Route(c, "numero");
        var b = await Api.Body(c);
        var cuentaId = Js.T(b["cuenta_bancaria_id"]) ? b.S("cuenta_bancaria_id") : null;

        if (cuentaId != null)
        {
            var owned = await Db.Query("SELECT id FROM cuentas_bancarias WHERE id = $1 AND admin_id = $2", cuentaId, adminId);
            if (owned.First == null) throw new AppError("Cuenta no encontrada", 404);
        }

        var r = await Db.Query(
            @"UPDATE departamentos SET cuenta_bancaria_id = $1
              WHERE admin_id = $2 AND numero = $3 RETURNING numero, cuenta_bancaria_id", cuentaId, adminId, numero);
        if (r.First == null) throw new AppError("Departamento no encontrado", 404);
        return Api.Data(r.First);
    }
}
