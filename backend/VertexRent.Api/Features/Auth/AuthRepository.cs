using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Todo el SQL de autenticación y registro. Sin reglas de negocio ni HTTP.</summary>
internal static class AuthRepository
{
    const string ColumnasSesion = "id, email, password_hash, nombre_completo, rol, avatar_url, activo, admin_id, permisos, token_version";

    public static async Task<Row?> UsuarioParaLogin(string email) =>
        (await Db.Query($"SELECT {ColumnasSesion} FROM usuarios WHERE email = $1", email)).First;

    public static async Task<Row?> UsuarioPorId(string id) =>
        (await Db.Query($"SELECT {ColumnasSesion} FROM usuarios WHERE id = $1", id)).First;

    public static Task RegistrarAcceso(string id) => Db.Query("UPDATE usuarios SET ultimo_acceso = NOW() WHERE id = $1", id);

    public static async Task<Row?> Perfil(string id) =>
        (await Db.Query(
            @"SELECT id, email, nombre_completo, rol, avatar_url, ultimo_acceso, created_at, permisos, rol_label
              FROM usuarios WHERE id = $1", id)).First;

    public static async Task<bool> ExisteEmail(string email) =>
        (await Db.Query("SELECT id FROM usuarios WHERE email = $1", email)).First != null;

    public static async Task<Row?> CodigoVigente(string codigo) =>
        (await Db.Query(
            @"SELECT id, rol, expira_en, admin_id, permisos, rol_label FROM codigos_invitacion
              WHERE codigo = $1 AND usado = false
                AND (expira_en IS NULL OR expira_en > NOW())", codigo)).First;

    /// <summary>Invitación del sistema anterior: el invitation_token (UUID) de un inquilino sin cuenta.</summary>
    public static async Task<Row?> InquilinoPorTokenAntiguo(string token) =>
        (await Db.Query("SELECT id, email_invitacion, admin_id FROM inquilinos WHERE invitation_token = $1 AND usuario_id IS NULL", token)).First;

    public static async Task<string?> PrimerAdmin() =>
        (await Db.Query("SELECT id FROM usuarios WHERE rol = 'admin' ORDER BY created_at ASC LIMIT 1")).First?.S("id");

    // ── Pasos del registro (dentro de una transacción) ───────────────────────
    /// <summary>Consume el código de forma atómica: false si otro registro ya lo usó.</summary>
    public static async Task<bool> ConsumirCodigo(DbTx tx, string codigoId) =>
        (await tx.Query("UPDATE codigos_invitacion SET usado = true, usado_en = NOW() WHERE id = $1 AND usado = false RETURNING id", codigoId)).First != null;

    public static async Task<Row> InsertarUsuario(DbTx tx, string email, string hash, string nombre, string rol, string? adminId, string permisosJson, string? rolLabel) =>
        (await tx.Query(
            @"INSERT INTO usuarios (email, password_hash, nombre_completo, rol, admin_id, permisos, rol_label)
              VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7)
              RETURNING id, email, nombre_completo, rol, avatar_url",
            email, hash, nombre, rol, adminId, permisosJson, rolLabel)).First!;

    public static Task MarcarCodigoUsadoPor(DbTx tx, string codigoId, string usuarioId) =>
        tx.Query("UPDATE codigos_invitacion SET usado_por = $1 WHERE id = $2", usuarioId, codigoId);

    public static async Task<bool> VincularInquilinoPorToken(DbTx tx, string usuarioId, string token) =>
        (await tx.Query(
            "UPDATE inquilinos SET usuario_id = $1, invitation_token = NULL WHERE invitation_token = $2 AND usuario_id IS NULL RETURNING id",
            usuarioId, token)).First != null;

    // ── Contraseña ───────────────────────────────────────────────────────────
    public static async Task<string?> HashDePassword(string usuarioId) =>
        (await Db.Query("SELECT password_hash FROM usuarios WHERE id = $1", usuarioId)).First?.S("password_hash");

    /// <summary>Guarda la contraseña nueva y sube token_version (cierra las demás sesiones).</summary>
    public static Task CambiarPassword(string usuarioId, string hash) =>
        Db.Query("UPDATE usuarios SET password_hash = $1, token_version = token_version + 1 WHERE id = $2", hash, usuarioId);
}
