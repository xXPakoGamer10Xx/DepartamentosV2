namespace VertexRent.Api;

/// <summary>Todo el SQL de las cuentas bancarias. Sin reglas de negocio ni HTTP.</summary>
internal static class CuentasRepository
{
    public static async Task<List<Row>> Listar(string adminId) =>
        (await Db.Query(
            @"SELECT cb.*,
                (SELECT json_agg(d.numero ORDER BY d.numero)
                   FROM departamentos d WHERE d.cuenta_bancaria_id = cb.id) AS departamentos,
                (SELECT COALESCE(SUM(i.renta), 0)
                   FROM departamentos d
                   JOIN inquilinos i ON i.depto_numero = d.numero AND i.admin_id = d.admin_id AND i.estado = 'activo'
                   WHERE d.cuenta_bancaria_id = cb.id) AS renta_mensual
              FROM cuentas_bancarias cb
              WHERE cb.admin_id = $1
              ORDER BY cb.es_predeterminada DESC, cb.created_at ASC", adminId)).Rows;

    public static async Task<Row?> Obtener(string id, string adminId) =>
        (await Db.Query("SELECT * FROM cuentas_bancarias WHERE id = $1 AND admin_id = $2", id, adminId)).First;

    public static async Task<bool> Existe(string id, string adminId) =>
        (await Db.Query("SELECT id FROM cuentas_bancarias WHERE id = $1 AND admin_id = $2", id, adminId)).First != null;

    public static async Task<int> Contar(string adminId) =>
        (await Db.Query("SELECT COUNT(*)::int AS n FROM cuentas_bancarias WHERE admin_id = $1", adminId)).First!.I("n");

    public static async Task<Row> Insertar(string adminId, string? alias, string? banco, string clabe, string? titular, bool predeterminada) =>
        (await Db.Query(
            @"INSERT INTO cuentas_bancarias (admin_id, alias, banco_nombre, banco_clabe, banco_titular, es_predeterminada)
              VALUES ($1, $2, $3, $4, $5, $6) RETURNING *", adminId, alias, banco, clabe, titular, predeterminada)).First!;

    public static async Task<Row> Actualizar(string id, string adminId, string? alias, string? banco, string? clabe, string? titular, bool activo) =>
        (await Db.Query(
            @"UPDATE cuentas_bancarias SET
                alias = $1, banco_nombre = $2, banco_clabe = $3, banco_titular = $4, activo = $5, updated_at = NOW()
              WHERE id = $6 AND admin_id = $7 RETURNING *", alias, banco, clabe, titular, activo, id, adminId)).First!;

    /// <summary>Deja una sola cuenta predeterminada (y activa), en una transacción.</summary>
    public static Task<Row?> HacerPredeterminada(string id, string adminId) =>
        Db.Tx(async tx =>
        {
            var existe = await tx.Query("SELECT id FROM cuentas_bancarias WHERE id = $1 AND admin_id = $2", id, adminId);
            if (existe.First == null) throw new AppError("Cuenta no encontrada", 404);
            await tx.Query("UPDATE cuentas_bancarias SET es_predeterminada = FALSE WHERE admin_id = $1", adminId);
            return (await tx.Query(
                @"UPDATE cuentas_bancarias SET es_predeterminada = TRUE, activo = TRUE, updated_at = NOW()
                  WHERE id = $1 AND admin_id = $2 RETURNING *", id, adminId)).First;
        });

    /// <summary>Los departamentos que la usaban quedan en NULL por ON DELETE SET NULL.</summary>
    public static Task Eliminar(string id, string adminId) =>
        Db.Query("DELETE FROM cuentas_bancarias WHERE id = $1 AND admin_id = $2", id, adminId);

    public static Task PromoverPrimeraActiva(string adminId) =>
        Db.Query(
            @"UPDATE cuentas_bancarias SET es_predeterminada = TRUE
              WHERE id = (SELECT id FROM cuentas_bancarias WHERE admin_id = $1 AND activo = TRUE
                          ORDER BY created_at ASC LIMIT 1)", adminId);

    /// <summary>Devuelve cuántos inquilinos pasaron a efectivo.</summary>
    public static async Task<int> PasarInquilinosAEfectivo(string adminId) =>
        (await Db.Query(
            @"UPDATE inquilinos SET metodo_pago = 'efectivo', updated_at = NOW()
              WHERE admin_id = $1 AND metodo_pago IN ('transferencia', 'ambos')", adminId)).RowCount;

    public static async Task<Row?> AsignarADepartamento(string adminId, string numero, string? cuentaId) =>
        (await Db.Query(
            @"UPDATE departamentos SET cuenta_bancaria_id = $1
              WHERE admin_id = $2 AND numero = $3 RETURNING numero, cuenta_bancaria_id", cuentaId, adminId, numero)).First;
}
