namespace VertexRent.Api;

/// <summary>Todo el SQL del módulo de cuotas extra. Sin reglas de negocio ni HTTP.</summary>
internal static class CuotasRepository
{
    public static async Task<List<Row>> Listar(string adminId, string? inquilinoId, string? estado)
    {
        var where = "WHERE i.admin_id = $1";
        var ps = new List<object?> { adminId };
        var idx = 2;
        if (inquilinoId != null) { where += $" AND c.inquilino_id = ${idx++}"; ps.Add(inquilinoId); }
        if (estado != null) { where += $" AND c.estado = ${idx++}"; ps.Add(estado); }
        return (await Db.Query(
            $@"SELECT c.*, u.nombre_completo as creado_por_nombre
               FROM cuotas_extra c
               JOIN inquilinos i ON i.id = c.inquilino_id
               LEFT JOIN usuarios u ON u.id = c.creado_por
               {where}
               ORDER BY c.created_at DESC", ps.ToArray())).Rows;
    }

    public static async Task<Row?> InquilinoDelPropietario(string? inquilinoId, string adminId) =>
        (await Db.Query("SELECT id, usuario_id, depto_numero FROM inquilinos WHERE id = $1 AND admin_id = $2", inquilinoId, adminId)).First;

    public static async Task<Row> Insertar(string? inquilinoId, string concepto, decimal monto, string periodo, string? actorId) =>
        (await Db.Query(
            "INSERT INTO cuotas_extra (inquilino_id, concepto, monto, periodo, creado_por) VALUES ($1, $2, $3, $4, $5) RETURNING *",
            inquilinoId, concepto, monto, periodo, actorId)).First!;

    /// <summary>Borra una cuota PENDIENTE del propietario; false si no existe, es ajena o ya se pagó.</summary>
    public static async Task<bool> EliminarPendiente(string id, string adminId) =>
        (await Db.Query(
            @"DELETE FROM cuotas_extra c
              USING inquilinos i
              WHERE c.id = $1 AND c.inquilino_id = i.id AND i.admin_id = $2 AND c.estado = 'pendiente'
              RETURNING c.id", id, adminId)).First != null;

    /// <summary>Marca como pagada una cuota pendiente en una sola sentencia (dos clics no la pagan dos veces).</summary>
    public static async Task<bool> MarcarPagada(string id, string adminId) =>
        (await Db.Query(
            @"UPDATE cuotas_extra c SET estado = 'pagado', pagado_en = NOW()
              FROM inquilinos i
              WHERE c.id = $1 AND c.inquilino_id = i.id AND i.admin_id = $2 AND c.estado = 'pendiente'
              RETURNING c.id", id, adminId)).First != null;
}
