using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Todo el SQL del módulo de departamentos. Sin reglas de negocio ni HTTP.</summary>
internal static class DepartamentosRepository
{
    public static async Task<List<Row>> Listar(string adminId) =>
        (await Db.Query(@"
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
            ORDER BY d.numero ASC", adminId)).Rows;

    public static async Task<Row?> PorNumeroConHistorial(string adminId, string numero) =>
        (await Db.Query(
            @"SELECT d.*,
                (SELECT json_agg(json_build_object(
                  'id', i.id, 'nombre', i.nombre_completo, 'renta', i.renta,
                  'fecha_inicio', i.fecha_inicio, 'fecha_termino', i.fecha_termino,
                  'estado', i.estado, 'created_at', i.created_at
                ) ORDER BY i.created_at DESC) FROM inquilinos i WHERE i.admin_id = d.admin_id AND i.depto_numero = d.numero) as historial_inquilinos
              FROM departamentos d WHERE d.admin_id = $1 AND d.numero = $2", adminId, numero)).First;

    public static async Task<Row?> Obtener(string adminId, string numero) =>
        (await Db.Query("SELECT * FROM departamentos WHERE admin_id = $1 AND numero = $2", adminId, numero)).First;

    public static async Task<Row> Insertar(string adminId, object? numero, string? descripcion, string inventario) =>
        (await Db.Query(
            @"INSERT INTO departamentos (admin_id, numero, descripcion, inventario_base)
              VALUES ($1, $2, $3, $4) RETURNING *", adminId, numero, descripcion, inventario)).First!;

    public static async Task<Row?> Actualizar(string adminId, string numero, string? estado, string? descripcion, string? inventario) =>
        (await Db.Query(
            @"UPDATE departamentos SET
                estado = $1, descripcion = $2, inventario_base = $3
              WHERE admin_id = $4 AND numero = $5 RETURNING *", estado, descripcion, inventario, adminId, numero)).First;

    public static async Task<bool> TieneInquilinosActivos(string adminId, string numero) =>
        (await Db.Query("SELECT id FROM inquilinos WHERE admin_id = $1 AND depto_numero = $2 AND estado = 'activo'", adminId, numero)).Rows.Count > 0;

    public static Task Eliminar(string adminId, string numero) =>
        Db.Query("DELETE FROM departamentos WHERE admin_id = $1 AND numero = $2", adminId, numero);

    public static async Task<Row?> Estadisticas(string adminId) =>
        (await Db.Query(@"
            SELECT
              COUNT(*) FILTER (WHERE estado = 'disponible') AS disponibles,
              COUNT(*) FILTER (WHERE estado = 'ocupado') AS ocupados,
              COUNT(*) FILTER (WHERE estado = 'mantenimiento') AS mantenimiento,
              COUNT(*) AS total,
              (SELECT COALESCE(SUM(renta), 0) FROM inquilinos WHERE admin_id = $1 AND estado = 'activo') AS ingresos_mensuales,
              (SELECT COUNT(*) FROM inquilinos WHERE admin_id = $1 AND fecha_termino > NOW() AND fecha_termino <= NOW() + INTERVAL '30 days' AND estado = 'activo') AS contratos_por_vencer,
              (SELECT COUNT(*) FROM inquilinos WHERE admin_id = $1 AND fecha_termino <= NOW() AND estado = 'activo') AS contratos_vencidos
            FROM departamentos WHERE admin_id = $1", adminId)).First;
}
