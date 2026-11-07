namespace VertexRent.Api;

public sealed record RecalculoPago(Row Pago, decimal TotalAbonado, bool Completo, decimal SaldoPendiente);

public static class SaldoService
{
    /// <summary>
    /// Recalcula el estado de un pago (periodo) a partir de la suma de sus abonos:
    /// transición pendiente/parcial/pagado (solo renta; las cuotas extra se cobran aparte).
    /// </summary>
    public static async Task<RecalculoPago> RecalcularPago(string pagoId, DbTx? tx = null)
    {
        var pago = await SaldoRepository.Pago(tx, pagoId) ?? throw new AppError("Pago no encontrado", 404);
        var totalAbonado = await SaldoRepository.TotalAbonado(tx, pagoId);
        var monto = pago.D("monto");
        var completo = totalAbonado >= monto - 0.01m;

        if (completo && !pago.B("confirmado"))
        {
            await SaldoRepository.MarcarConfirmado(tx, pagoId);
        }
        else if (!completo && pago.B("confirmado"))
        {
            // Se editó/eliminó un abono y el pago dejó de estar completo: reabrir.
            await SaldoRepository.Reabrir(tx, pagoId);
        }

        var refreshed = (await SaldoRepository.Pago(tx, pagoId))!;
        return new RecalculoPago(refreshed, totalAbonado, completo, Math.Max(monto - totalAbonado, 0));
    }

    /// <summary>
    /// Abona el saldo pendiente del pago (o solo la parte de renta si se indica <paramref name="rentaTope"/>).
    /// Bloquea la fila del pago: dos confirmaciones simultáneas no duplican el abono.
    /// </summary>
    public static Task<RecalculoPago> AbonarSaldoPendiente(string pagoId, string? metodo, string nota, string? actor, decimal? rentaTope = null) =>
        Db.Tx(async tx =>
        {
            var pago = await SaldoRepository.PagoBloqueado(tx, pagoId) ?? throw new AppError("Pago no encontrado", 404);
            var total = await SaldoRepository.TotalAbonado(tx, pagoId);
            var saldo = Math.Max(pago.D("monto") - total, 0);
            var monto = rentaTope is { } tope ? Math.Min(Math.Max(tope - total, 0), saldo) : saldo;
            if (monto > 0)
                await SaldoRepository.InsertarAbonoDeSaldo(tx, pagoId, monto, metodo, nota, actor);
            return await RecalcularPago(pagoId, tx);
        });

    /// <summary>Registra un abono de monto libre con el pago bloqueado, y recalcula el estado.</summary>
    public static Task<(Row Abono, RecalculoPago Rec)> RegistrarAbono(
        string pagoId, decimal monto, string? fecha, string? metodo, string? nota, string? comprobante, string? actor) =>
        Db.Tx(async tx =>
        {
            var pago = await SaldoRepository.PagoBloqueado(tx, pagoId) ?? throw new AppError("Pago no encontrado", 404);
            if (pago.B("confirmado")) throw new AppError("Este periodo ya está pagado por completo", 400);
            var abono = await SaldoRepository.InsertarAbono(tx, pagoId, monto, fecha, metodo, nota, comprobante, actor);
            return (abono, await RecalcularPago(pagoId, tx));
        });
}
