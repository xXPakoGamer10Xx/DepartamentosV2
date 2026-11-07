namespace VertexRent.Api;

/// <summary>Todo el SQL del módulo de usuarios. Sin reglas de negocio ni HTTP.</summary>
internal static class UsuariosRepository
{
    public static async Task<List<Row>> Listar(string adminId) =>
        (await Db.Query(
            @"SELECT u.id, u.email, u.nombre_completo, u.rol, u.avatar_url, u.activo, u.ultimo_acceso, u.created_at, u.permisos, u.rol_label
              FROM usuarios u
              WHERE u.id = $1
                 OR u.admin_id = $1
                 OR u.id IN (
                   SELECT usado_por FROM codigos_invitacion WHERE admin_id = $1 AND usado_por IS NOT NULL
                 )
              ORDER BY u.created_at ASC", adminId)).Rows;

    /// <summary>El usuario (con su rol) si pertenece al alcance del propietario: lo creó él o se registró con su invitación.</summary>
    public static async Task<Row?> EnAlcance(string adminId, string id) =>
        (await Db.Query(
            @"SELECT rol FROM usuarios WHERE id = $2 AND (admin_id = $1
                OR id IN (SELECT usado_por FROM codigos_invitacion WHERE admin_id = $1 AND usado_por IS NOT NULL))",
            adminId, id)).First;

    public static async Task<bool> ExisteEmail(string email) =>
        (await Db.Query("SELECT id FROM usuarios WHERE email = $1", email)).First != null;

    public static async Task<Row> Insertar(string email, string hash, string nombre, string rol, string? adminId, string permisosJson, string? rolLabel) =>
        (await Db.Query(
            @"INSERT INTO usuarios (email, password_hash, nombre_completo, rol, admin_id, permisos, rol_label)
              VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7)
              RETURNING id, email, nombre_completo, rol, activo, created_at, permisos, rol_label",
            email, hash, nombre, rol, adminId, permisosJson, rolLabel)).First!;

    public static async Task<Row?> AlternarActivo(string id) =>
        (await Db.Query("UPDATE usuarios SET activo = NOT activo WHERE id = $1 RETURNING id, email, activo", id)).First;

    public static async Task<Row?> CambiarRol(string id, string rol, string adminId) =>
        (await Db.Query(
            @"UPDATE usuarios SET rol = $1, admin_id = COALESCE(admin_id, $3) WHERE id = $2
              RETURNING id, email, nombre_completo, rol, activo", rol, id, adminId)).First;

    public static async Task<Row?> ActualizarPerfil(string id, string? nombre, string? avatar) =>
        (await Db.Query(
            @"UPDATE usuarios SET
                nombre_completo = COALESCE($1, nombre_completo),
                avatar_url = COALESCE($2, avatar_url)
              WHERE id = $3
              RETURNING id, email, nombre_completo, rol, avatar_url", nombre, avatar, id)).First;

    public static async Task<Row?> ActualizarPermisos(string id, string permisosJson, string? rolLabel, string adminId) =>
        (await Db.Query(
            @"UPDATE usuarios SET permisos = $1::jsonb, rol_label = $4, admin_id = COALESCE(admin_id, $3) WHERE id = $2
              RETURNING id, email, nombre_completo, rol, activo, permisos, rol_label",
            permisosJson, id, adminId, rolLabel)).First;

    public static async Task<bool> Eliminar(string id) =>
        (await Db.Query("DELETE FROM usuarios WHERE id = $1 RETURNING id", id)).First != null;
}
