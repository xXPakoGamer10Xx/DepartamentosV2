namespace VertexRent.Api;

/// <summary>SQL del recálculo de saldos y de los abonos. Acepta una transacción opcional (<see cref="DbTx"/>).</summary>
internal static class SaldoRepository
{
    static Task<QueryResult> Q(DbTx? tx, string sql, params object?[] args) => tx != null ? tx.Query(sql, args) : Db.Query(sql, args);

    public static async Task<Row?> Pago(DbTx? tx, string pagoId) =>
        (await Q(tx, "SELECT * FROM pagos WHERE id = $1", pagoId)).First;

    /// <summary>Lee el pago y lo bloquea hasta terminar la transacción (dos confirmaciones simultáneas no se pisan).</summary>
    public static async Task<Row?> PagoBloqueado(DbTx tx, string pagoId) =>
        (await tx.Query("SELECT * FROM pagos WHERE id = $1 FOR UPDATE", pagoId)).First;

    public static async Task<decimal> TotalAbonado(DbTx? tx, string pagoId) =>
        (await Q(tx, "SELECT COALESCE(SUM(monto), 0) AS total FROM abonos_pago WHERE pago_id = $1", pagoId)).First!.D("total");

    public static Task MarcarConfirmado(DbTx? tx, string pagoId) =>
        Q(tx, "UPDATE pagos SET confirmado = true, confirmado_en = NOW() WHERE id = $1", pagoId);

    /// <summary>Se editó o eliminó un abono y el pago dejó de estar completo.</summary>
    public static Task Reabrir(DbTx? tx, string pagoId) =>
        Q(tx, "UPDATE pagos SET confirmado = false, confirmado_en = NULL WHERE id = $1", pagoId);

    public static Task InsertarAbonoDeSaldo(DbTx tx, string pagoId, decimal monto, string? metodo, string nota, string? actorId) =>
        tx.Query("INSERT INTO abonos_pago (pago_id, monto, metodo, nota, registrado_por) VALUES ($1, $2, $3, $4, $5)",
            pagoId, monto, metodo, nota, actorId);

    public static async Task<Row> InsertarAbono(DbTx tx, string pagoId, decimal monto, string? fecha, string? metodo, string? nota, string? comprobante, string? actorId) =>
        (await tx.Query(
            @"INSERT INTO abonos_pago (pago_id, monto, fecha, metodo, nota, comprobante_url, registrado_por)
              VALUES ($1, $2, COALESCE($3, CURRENT_DATE), $4, $5, $6, $7) RETURNING *",
            pagoId, monto, fecha, metodo, nota, comprobante, actorId)).First!;
}
