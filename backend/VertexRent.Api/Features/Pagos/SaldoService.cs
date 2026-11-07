namespace VertexRent.Api;

public sealed record RecalculoPago(Row Pago, decimal TotalAbonado, bool Completo, decimal SaldoPendiente);

public static class SaldoService
{
    static Task<QueryResult> Q(DbTx? tx, string sql, params object?[] args) => tx != null ? tx.Query(sql, args) : Db.Query(sql, args);

    /// <summary>
    /// Recalcula el estado de un pago (periodo) a partir de la suma de sus abonos:
    /// transición pendiente/parcial/pagado (solo renta; las cuotas extra se cobran aparte).
    /// </summary>
    public static async Task<RecalculoPago> RecalcularPago(string pagoId, DbTx? tx = null)
    {
        var pago = (await Q(tx, "SELECT * FROM pagos WHERE id = $1", pagoId)).First
            ?? throw new AppError("Pago no encontrado", 404);

        var sum = await Q(tx, "SELECT COALESCE(SUM(monto), 0) AS total FROM abonos_pago WHERE pago_id = $1", pagoId);
        var totalAbonado = sum.First!.D("total");
        var monto = pago.D("monto");
        var completo = totalAbonado >= monto - 0.01m;

        if (completo && !pago.B("confirmado"))
        {
            await Q(tx, "UPDATE pagos SET confirmado = true, confirmado_en = NOW() WHERE id = $1", pagoId);
        }
        else if (!completo && pago.B("confirmado"))
        {
            // Se editó/eliminó un abono y el pago dejó de estar completo: reabrir.
            await Q(tx, "UPDATE pagos SET confirmado = false, confirmado_en = NULL WHERE id = $1", pagoId);
        }

        var refreshed = (await Q(tx, "SELECT * FROM pagos WHERE id = $1", pagoId)).First!;
        return new RecalculoPago(refreshed, totalAbonado, completo, Math.Max(monto - totalAbonado, 0));
    }

    /// <summary>
    /// Abona el saldo pendiente del pago (o solo la parte de renta si se indica <paramref name="rentaTope"/>).
    /// Bloquea la fila del pago: dos confirmaciones simultáneas no duplican el abono.
    /// </summary>
    public static Task<RecalculoPago> AbonarSaldoPendiente(string pagoId, string? metodo, string nota, string? actor, decimal? rentaTope = null) =>
        Db.Tx(async tx =>
        {
            var pago = (await tx.Query("SELECT * FROM pagos WHERE id = $1 FOR UPDATE", pagoId)).First
                ?? throw new AppError("Pago no encontrado", 404);
            var total = (await tx.Query("SELECT COALESCE(SUM(monto), 0) AS total FROM abonos_pago WHERE pago_id = $1", pagoId)).First!.D("total");
            var saldo = Math.Max(pago.D("monto") - total, 0);
            var monto = rentaTope is { } tope ? Math.Min(Math.Max(tope - total, 0), saldo) : saldo;
            if (monto > 0)
                await tx.Query(
                    "INSERT INTO abonos_pago (pago_id, monto, metodo, nota, registrado_por) VALUES ($1, $2, $3, $4, $5)",
                    pagoId, monto, metodo, nota, actor);
            return await RecalcularPago(pagoId, tx);
        });

    /// <summary>Registra un abono de monto libre con el pago bloqueado, y recalcula el estado.</summary>
    public static Task<(Row Abono, RecalculoPago Rec)> RegistrarAbono(
        string pagoId, decimal monto, string? fecha, string? metodo, string? nota, string? comprobante, string? actor) =>
        Db.Tx(async tx =>
        {
            var pago = (await tx.Query("SELECT confirmado FROM pagos WHERE id = $1 FOR UPDATE", pagoId)).First
                ?? throw new AppError("Pago no encontrado", 404);
            if (pago.B("confirmado")) throw new AppError("Este periodo ya está pagado por completo", 400);
            var abono = (await tx.Query(
                @"INSERT INTO abonos_pago (pago_id, monto, fecha, metodo, nota, comprobante_url, registrado_por)
                  VALUES ($1, $2, COALESCE($3, CURRENT_DATE), $4, $5, $6, $7) RETURNING *",
                pagoId, monto, fecha, metodo, nota, comprobante, actor)).First!;
            return (abono, await RecalcularPago(pagoId, tx));
        });
}
