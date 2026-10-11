namespace VertexRent.Api;

public sealed record RecalculoPago(Row Pago, decimal TotalAbonado, bool Completo, decimal SaldoPendiente);

public static class SaldoService
{
    /// <summary>
    /// Recalcula el estado de un pago (periodo) a partir de la suma de sus abonos:
    /// transición pendiente/parcial/pagado y marcado de cuotas extra del mismo periodo.
    /// </summary>
    public static async Task<RecalculoPago> RecalcularPago(string pagoId)
    {
        var pago = (await Db.Query("SELECT * FROM pagos WHERE id = $1", pagoId)).First
            ?? throw new Exception($"Pago {pagoId} no encontrado");

        var sum = await Db.Query("SELECT COALESCE(SUM(monto), 0) AS total FROM abonos_pago WHERE pago_id = $1", pagoId);
        var totalAbonado = sum.First!.D("total");
        var monto = pago.D("monto");
        var completo = totalAbonado >= monto - 0.01m;

        if (completo && !pago.B("confirmado"))
        {
            await Db.Query("UPDATE pagos SET confirmado = true, confirmado_en = NOW() WHERE id = $1", pagoId);
            await Db.Query(
                @"UPDATE cuotas_extra SET estado = 'pagado', pagado_en = NOW()
                  WHERE inquilino_id = $1 AND periodo = $2 AND estado = 'pendiente'",
                pago.S("inquilino_id"), pago.S("periodo"));
        }
        else if (!completo && pago.B("confirmado"))
        {
            // Se editó/eliminó un abono y el pago dejó de estar completo: reabrir.
            await Db.Query("UPDATE pagos SET confirmado = false, confirmado_en = NULL WHERE id = $1", pagoId);
        }

        var refreshed = (await Db.Query("SELECT * FROM pagos WHERE id = $1", pagoId)).First!;
        return new RecalculoPago(refreshed, totalAbonado, completo, Math.Max(monto - totalAbonado, 0));
    }
}
