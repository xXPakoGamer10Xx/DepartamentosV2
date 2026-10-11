using System.Text.Json.Nodes;
using Npgsql;

namespace VertexRent.Api;

public static class DepartamentosEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/departamentos").Auth();

        g.MapGet("/stats", H(Stats)).Permiso("departamentos");
        g.MapGet("/", H(GetAll)).Permiso("departamentos");
        g.MapGet("/{numero}", H(GetByNumero)).Permiso("departamentos");
        g.MapPost("/", H(Create)).AdminOnly();
        g.MapPut("/{numero}", H(Update)).AdminOnly();
        g.MapDelete("/{numero}", H(Delete)).AdminOnly();
    }

    static async Task<IResult> GetAll(HttpContext c)
    {
        var r = await Db.Query(@"
            SELECT d.*,
              (SELECT json_build_object(
                'id', i.id,
                'nombre', i.nombre_completo,
                'renta', i.renta,
                'fecha_termino', i.fecha_termino,
                'tel', i.tel_arrendatario,
                'estado', i.estado
              ) FROM inquilinos i WHERE i.admin_id = d.admin_id AND i.depto_numero = d.numero AND i.estado = 'activo'
              ORDER BY i.created_at DESC LIMIT 1) as inquilino_actual
            FROM departamentos d
            WHERE d.admin_id = $1
            ORDER BY d.numero ASC", c.Me().Id);
        return Api.Data(r.Rows);
    }

    static async Task<IResult> GetByNumero(HttpContext c)
    {
        var r = await Db.Query(
            @"SELECT d.*,
                (SELECT json_agg(json_build_object(
                  'id', i.id, 'nombre', i.nombre_completo, 'renta', i.renta,
                  'fecha_inicio', i.fecha_inicio, 'fecha_termino', i.fecha_termino,
                  'estado', i.estado, 'created_at', i.created_at
                ) ORDER BY i.created_at DESC) FROM inquilinos i WHERE i.admin_id = d.admin_id AND i.depto_numero = d.numero) as historial_inquilinos
              FROM departamentos d WHERE d.admin_id = $1 AND d.numero = $2",
            c.Me().Id, Api.Route(c, "numero"));
        if (r.First == null) throw new AppError("Departamento no encontrado", 404);
        return Api.Data(r.First);
    }

    static async Task<IResult> Update(HttpContext c)
    {
        var numero = Api.Route(c, "numero");
        var b = await Api.Body(c);
        var adminId = c.Me().Id;

        var cur = await Db.Query("SELECT * FROM departamentos WHERE admin_id = $1 AND numero = $2", adminId, numero);
        var d = cur.First ?? throw new AppError("Departamento no encontrado", 404);

        var inv = b.ContainsKey("inventario_base") ? b["inventario_base"]?.ToJsonString() ?? "null" : d.J("inventario_base")?.ToJsonString();
        var r = await Db.Query(
            @"UPDATE departamentos SET
                estado = $1, descripcion = $2, inventario_base = $3
              WHERE admin_id = $4 AND numero = $5 RETURNING *",
            b["estado"] is null ? d.S("estado") : b.S("estado"),
            b["descripcion"] is null ? d.S("descripcion") : b.S("descripcion"),
            inv, adminId, numero);
        return Api.Data(r.First);
    }

    static async Task<IResult> Create(HttpContext c)
    {
        var b = await Api.Body(c);
        if (!Js.T(b["numero"])) throw new AppError("El número de departamento es requerido", 400);
        try
        {
            var r = await Db.Query(
                @"INSERT INTO departamentos (admin_id, numero, descripcion, inventario_base)
                  VALUES ($1, $2, $3, $4) RETURNING *",
                c.Me().Id, b["numero"], Js.T(b["descripcion"]) ? b.S("descripcion") : null,
                (Js.T(b["inventario_base"]) ? b["inventario_base"]! : new JsonArray()).ToJsonString());
            return Api.Data(r.First, 201);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new AppError($"El departamento número {b.S("numero")} ya existe", 409);
        }
    }

    static async Task<IResult> Delete(HttpContext c)
    {
        var numero = Api.Route(c, "numero");
        var adminId = c.Me().Id;
        var existing = await Db.Query("SELECT * FROM departamentos WHERE admin_id = $1 AND numero = $2", adminId, numero);
        if (existing.First == null) throw new AppError("Departamento no encontrado", 404);

        var activos = await Db.Query(
            "SELECT id FROM inquilinos WHERE admin_id = $1 AND depto_numero = $2 AND estado = 'activo'", adminId, numero);
        if (activos.Rows.Count > 0)
            throw new AppError("No se puede eliminar un departamento con inquilinos activos", 400);

        await Db.Query("DELETE FROM departamentos WHERE admin_id = $1 AND numero = $2", adminId, numero);
        return Api.Msg("Departamento eliminado correctamente");
    }

    static async Task<IResult> Stats(HttpContext c)
    {
        var r = await Db.Query(@"
            SELECT
              COUNT(*) FILTER (WHERE estado = 'disponible') AS disponibles,
              COUNT(*) FILTER (WHERE estado = 'ocupado') AS ocupados,
              COUNT(*) FILTER (WHERE estado = 'mantenimiento') AS mantenimiento,
              COUNT(*) AS total,
              (SELECT COALESCE(SUM(renta), 0) FROM inquilinos WHERE admin_id = $1 AND estado = 'activo') AS ingresos_mensuales,
              (SELECT COUNT(*) FROM inquilinos WHERE admin_id = $1 AND fecha_termino > NOW() AND fecha_termino <= NOW() + INTERVAL '30 days' AND estado = 'activo') AS contratos_por_vencer,
              (SELECT COUNT(*) FROM inquilinos WHERE admin_id = $1 AND fecha_termino <= NOW() AND estado = 'activo') AS contratos_vencidos
            FROM departamentos WHERE admin_id = $1", c.Me().Id);
        return Api.Data(r.First);
    }
}
