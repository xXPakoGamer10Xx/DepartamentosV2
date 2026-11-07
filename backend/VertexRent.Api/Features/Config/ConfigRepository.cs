namespace VertexRent.Api;

/// <summary>Todo el SQL de la configuración del propietario. Sin reglas de negocio ni HTTP.</summary>
internal static class ConfigRepository
{
    public static async Task<List<Row>> Claves(string adminId) =>
        (await Db.Query("SELECT clave, valor FROM configuracion WHERE admin_id = $1 ORDER BY clave", adminId)).Rows;

    public static async Task<Row?> InquilinoActivoDelUsuario(string usuarioId) =>
        (await Db.Query("SELECT admin_id, depto_numero FROM inquilinos WHERE usuario_id = $1 AND estado = 'activo' LIMIT 1", usuarioId)).First;

    /// <summary>Cuenta bancaria que aplica a un departamento: la asignada o, si no, la predeterminada.</summary>
    public static async Task<Row?> CuentaDelDepartamento(string adminId, string numero) =>
        (await Db.Query(
            @"SELECT cb.banco_nombre, cb.banco_clabe, cb.banco_titular
              FROM cuentas_bancarias cb
              WHERE cb.admin_id = $1 AND cb.activo = TRUE
                AND (
                  cb.id = (SELECT d.cuenta_bancaria_id FROM departamentos d
                           WHERE d.admin_id = $1 AND d.numero = $2)
                  OR cb.es_predeterminada = TRUE
                )
              ORDER BY (cb.id = (SELECT d.cuenta_bancaria_id FROM departamentos d
                                 WHERE d.admin_id = $1 AND d.numero = $2)) DESC
              LIMIT 1", adminId, numero)).First;

    public static Task Guardar(string adminId, string clave, string valor) =>
        Db.Query(
            @"INSERT INTO configuracion (admin_id, clave, valor) VALUES ($1, $2, $3)
              ON CONFLICT (admin_id, clave) DO UPDATE SET valor = $3, updated_at = NOW()", adminId, clave, valor);
}
