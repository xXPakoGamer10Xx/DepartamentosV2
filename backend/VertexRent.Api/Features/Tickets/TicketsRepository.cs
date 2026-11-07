namespace VertexRent.Api;

/// <summary>Todo el SQL del módulo de tickets. Sin reglas de negocio ni HTTP.</summary>
internal static class TicketsRepository
{
    const string SelectAdmin = @"
        SELECT t.*,
          i.nombre_completo as inquilino_nombre,
          i.depto_numero,
          u.nombre_completo as atendido_por_nombre
        FROM tickets t
        JOIN inquilinos i ON i.id = t.inquilino_id
        LEFT JOIN usuarios u ON u.id = t.atendido_por
        WHERE i.admin_id = $1";

    /// <summary>Con <paramref name="mes"/> (AAAA-MM) se ordena por fecha; sin él, abiertos primero.</summary>
    public static async Task<List<Row>> DelPropietario(string adminId, string? estado, string? mes)
    {
        if (mes != null)
        {
            var partes = mes.Split('-');
            var sql = SelectAdmin + @"
                AND DATE_PART('year', t.created_at) = $2
                AND DATE_PART('month', t.created_at) = $3";
            var ps = new List<object?> { adminId, partes[0], partes.Length > 1 ? partes[1] : null };
            if (estado != null) { sql += " AND t.estado = $4"; ps.Add(estado); }
            return (await Db.Query(sql + " ORDER BY t.created_at DESC", ps.ToArray())).Rows;
        }
        var sql2 = SelectAdmin;
        var ps2 = new List<object?> { adminId };
        if (estado != null) { sql2 += " AND t.estado = $2"; ps2.Add(estado); }
        sql2 += @"
            ORDER BY
              CASE t.estado WHEN 'abierto' THEN 0 WHEN 'en_revision' THEN 1 ELSE 2 END,
              t.created_at DESC";
        return (await Db.Query(sql2, ps2.ToArray())).Rows;
    }

    public static async Task<List<Row>> DelInquilino(string inquilinoId) =>
        (await Db.Query("SELECT t.* FROM tickets t WHERE t.inquilino_id = $1 ORDER BY t.created_at DESC", inquilinoId)).Rows;

    public static async Task<string?> InquilinoActivoDelUsuario(string usuarioId) =>
        (await Db.Query("SELECT id FROM inquilinos WHERE usuario_id = $1 AND estado = 'activo' LIMIT 1", usuarioId)).First?.S("id");

    public static async Task<bool> InquilinoDelPropietario(string? inquilinoId, string adminId) =>
        (await Db.Query("SELECT id FROM inquilinos WHERE id = $1 AND admin_id = $2", inquilinoId, adminId)).First != null;

    public static async Task<Row> Insertar(string inquilinoId, string titulo, string descripcion, string? actorId) =>
        (await Db.Query("INSERT INTO tickets (inquilino_id, titulo, descripcion, creado_por) VALUES ($1, $2, $3, $4) RETURNING *",
            inquilinoId, titulo, descripcion, actorId)).First!;

    /// <summary>El ticket con datos del inquilino y de su propietario (para avisar).</summary>
    public static async Task<Row?> ConPropietario(string ticketId) =>
        (await Db.Query(
            @"SELECT t.*, i.nombre_completo as inquilino_nombre, i.depto_numero, i.admin_id
              FROM tickets t JOIN inquilinos i ON i.id = t.inquilino_id WHERE t.id = $1", ticketId)).First;

    public static async Task<bool> EsDelPropietario(string ticketId, string adminId) =>
        (await Db.Query("SELECT t.id FROM tickets t JOIN inquilinos i ON i.id = t.inquilino_id WHERE t.id = $1 AND i.admin_id = $2",
            ticketId, adminId)).First != null;

    public static async Task<Row> Actualizar(string id, string? estado, string? nota, string? actorId) =>
        (await Db.Query(
            @"UPDATE tickets SET
                estado       = COALESCE($1, estado),
                nota_admin   = COALESCE($2, nota_admin),
                atendido_por = CASE WHEN $1 IS NOT NULL THEN $3 ELSE atendido_por END,
                updated_at   = NOW()
              WHERE id = $4 RETURNING *", estado, nota, actorId, id)).First!;

    public static async Task<string?> UsuarioDelInquilino(string ticketId) =>
        (await Db.Query("SELECT i.usuario_id FROM inquilinos i JOIN tickets t ON t.inquilino_id = i.id WHERE t.id = $1", ticketId)).First?.S("usuario_id");

    public static async Task<bool> EliminarDelPropietario(string id, string adminId) =>
        (await Db.Query("DELETE FROM tickets t USING inquilinos i WHERE t.id = $1 AND t.inquilino_id = i.id AND i.admin_id = $2 RETURNING t.id",
            id, adminId)).First != null;

    /// <summary>El inquilino solo borra el suyo y durante los primeros 5 minutos.</summary>
    public static async Task<bool> EliminarRecienCreado(string id, string inquilinoId) =>
        (await Db.Query(
            @"DELETE FROM tickets
              WHERE id = $1 AND inquilino_id = $2 AND created_at > NOW() - INTERVAL '5 minutes'
              RETURNING id", id, inquilinoId)).First != null;
}
