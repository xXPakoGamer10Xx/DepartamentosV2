namespace VertexRent.Api;

/// <summary>Todo el SQL de notificaciones y suscripciones Web Push. Sin reglas de negocio ni HTTP.</summary>
internal static class NotificacionesRepository
{
    // ── Bandeja del usuario ──────────────────────────────────────────────────
    public static Task LimpiarLeidasAntiguas(string usuarioId) =>
        Db.Query("DELETE FROM notificaciones WHERE usuario_id = $1 AND leido = true AND created_at < now() - interval '7 days'", usuarioId);

    public static async Task<List<Row>> Recientes(string usuarioId) =>
        (await Db.Query(
            @"SELECT * FROM notificaciones
              WHERE usuario_id = $1
              ORDER BY created_at DESC
              LIMIT 50", usuarioId)).Rows;

    public static Task MarcarTodasLeidas(string usuarioId) =>
        Db.Query("UPDATE notificaciones SET leido = true WHERE usuario_id = $1", usuarioId);

    public static Task EliminarLeidas(string usuarioId) =>
        Db.Query("DELETE FROM notificaciones WHERE usuario_id = $1 AND leido = true", usuarioId);

    public static Task Eliminar(string id, string usuarioId) =>
        Db.Query("DELETE FROM notificaciones WHERE id = $1 AND usuario_id = $2", id, usuarioId);

    /// <summary>Marca como leída una notificación propia; null si no existe o es de otro usuario.</summary>
    public static async Task<Row?> MarcarLeida(string id, string usuarioId) =>
        (await Db.Query("UPDATE notificaciones SET leido = true WHERE id = $1 AND usuario_id = $2 RETURNING *", id, usuarioId)).First;

    // ── Envío ────────────────────────────────────────────────────────────────
    public static Task Insertar(string usuarioId, string titulo, string mensaje, string tipo, string? dedupeKey) =>
        Db.Query("INSERT INTO notificaciones (usuario_id, titulo, mensaje, tipo, dedupe_key) VALUES ($1, $2, $3, $4, $5)",
            usuarioId, titulo, mensaje, tipo, dedupeKey);

    /// <summary>Colaboradores activos del propietario que reciben los avisos de cobranza.</summary>
    public static async Task<List<string>> ColaboradoresDeCobranza(string adminId) =>
        (await Db.Query(
            @"SELECT id FROM usuarios
              WHERE admin_id = $1 AND rol = 'cobrador' AND activo = TRUE
                AND permisos @> '[""notificaciones.pagos""]'::jsonb", adminId)).Rows.Select(r => r.S("id")!).ToList();

    // ── Suscripciones Web Push ───────────────────────────────────────────────
    public static async Task<List<Row>> Suscripciones(string usuarioId) =>
        (await Db.Query("SELECT endpoint, p256dh, auth FROM web_push_subscriptions WHERE usuario_id = $1", usuarioId)).Rows;

    /// <summary>Un navegador (endpoint) pertenece a la última cuenta que lo registró.</summary>
    public static Task GuardarSuscripcion(string usuarioId, string? endpoint, string? p256dh, string? auth) =>
        Db.Query(
            @"INSERT INTO web_push_subscriptions (usuario_id, endpoint, p256dh, auth)
              VALUES ($1, $2, $3, $4)
              ON CONFLICT (endpoint) DO UPDATE SET usuario_id = $1, p256dh = $3, auth = $4",
            usuarioId, endpoint, p256dh, auth);

    public static Task EliminarSuscripciones(List<string> endpoints) =>
        Db.Query("DELETE FROM web_push_subscriptions WHERE endpoint = ANY($1)", endpoints);
}
