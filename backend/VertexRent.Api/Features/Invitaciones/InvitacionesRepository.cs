namespace VertexRent.Api;

/// <summary>Todo el SQL de los códigos de invitación. Sin reglas de negocio ni HTTP.</summary>
internal static class InvitacionesRepository
{
    public static async Task<bool> ExisteCodigo(string codigo) =>
        (await Db.Query("SELECT 1 FROM codigos_invitacion WHERE codigo = $1", codigo)).Rows.Count > 0;

    public static async Task<Row> Insertar(string adminId, string codigo, string rol, DateTime? expiraEn, string permisosJson, string? rolLabel) =>
        (await Db.Query(
            @"INSERT INTO codigos_invitacion (admin_id, codigo, rol, expira_en, permisos, rol_label)
              VALUES ($1, $2, $3, $4, $5::jsonb, $6) RETURNING *",
            adminId, codigo, rol, expiraEn, permisosJson, rolLabel)).First!;

    public static async Task<List<Row>> Listar(string adminId) =>
        (await Db.Query(
            @"SELECT ci.*,
                     u.nombre_completo AS usado_por_nombre
              FROM codigos_invitacion ci
              LEFT JOIN usuarios u ON u.id = ci.usado_por
              WHERE ci.admin_id = $1
              ORDER BY ci.created_at DESC", adminId)).Rows;

    public static async Task<bool> Revocar(string id, string adminId) =>
        (await Db.Query("DELETE FROM codigos_invitacion WHERE id = $1 AND admin_id = $2 RETURNING id", id, adminId)).First != null;
}
