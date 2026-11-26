using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Todo el SQL del módulo de depósitos. Sin reglas de negocio ni HTTP.</summary>
internal static class DepositosRepository
{
    static Task<QueryResult> Q(DbTx? tx, string sql, params object?[] args) => tx != null ? tx.Query(sql, args) : Db.Query(sql, args);

    public static Task<decimal> TotalPagado(string inquilinoId) => TotalPagado(null, inquilinoId);

    public static async Task<decimal> TotalPagado(DbTx? tx, string inquilinoId) =>
        (await Q(tx, "SELECT COALESCE(SUM(monto), 0) AS pagado FROM abonos_deposito WHERE inquilino_id = $1", inquilinoId)).First!.D("pagado");

    public static async Task<List<Row>> Abonos(string inquilinoId) =>
        (await Db.Query(
            @"SELECT a.id, a.inquilino_id, a.monto, a.fecha, a.metodo, a.nota, a.registrado_por, a.created_at,
                     (a.comprobante_url IS NOT NULL) AS tiene_comprobante,
                     u.nombre_completo AS registrado_por_nombre
              FROM abonos_deposito a
              LEFT JOIN usuarios u ON u.id = a.registrado_por
              WHERE a.inquilino_id = $1
              ORDER BY a.fecha ASC, a.created_at ASC", inquilinoId)).Rows;

    public static Task<Row> Insertar(string inquilinoId, decimal monto, string? fecha, string? metodo, string? nota, string? comprobante, string? actorId) =>
        Insertar(null, inquilinoId, monto, fecha, metodo, nota, comprobante, actorId);

    public static async Task<Row> Insertar(DbTx? tx, string inquilinoId, decimal monto, string? fecha, string? metodo, string? nota, string? comprobante, string? actorId) =>
        (await Q(tx,
            @"INSERT INTO abonos_deposito (inquilino_id, monto, fecha, metodo, nota, comprobante_url, registrado_por)
              VALUES ($1, $2, COALESCE($3, CURRENT_DATE), $4, $5, $6, $7) RETURNING *",
            inquilinoId, monto, fecha, metodo, nota, comprobante, actorId)).First!;

    /// <summary>Bloquea la fila del inquilino activo del propietario para serializar repartos semanales.</summary>
    public static async Task<Row?> InquilinoActivoBloqueado(DbTx tx, string inquilinoId, string adminId) =>
        (await tx.Query(
            "SELECT *, fecha_inicio::text AS fecha_inicio_txt FROM inquilinos WHERE id = $1 AND admin_id = $2 AND estado = 'activo' FOR UPDATE",
            inquilinoId, adminId)).First;

    /// <summary>Periodos desde el inicio del contrato hasta el mes actual (inclusive) con saldo de renta pendiente.</summary>
    public static async Task<List<Row>> PeriodosRentaPendientesEnTx(DbTx tx, string inquilinoId, string fechaInicioTxt, decimal renta) =>
        (await tx.Query(
            @"WITH periodos AS (
                SELECT to_char(gs, 'YYYY-MM') AS periodo
                FROM generate_series(
                  date_trunc('month', $2::date),
                  GREATEST(date_trunc('month', $2::date), date_trunc('month', CURRENT_DATE)),
                  interval '1 month'
                ) AS gs
              ),
              abonado AS (
                SELECT p.periodo, p.monto, COALESCE(SUM(a.monto), 0) AS total
                FROM pagos p LEFT JOIN abonos_pago a ON a.pago_id = p.id
                WHERE p.inquilino_id = $1 GROUP BY p.periodo, p.monto
              )
              SELECT pe.periodo,
                     COALESCE(ab.monto, $3::numeric) AS monto_esperado,
                     COALESCE(ab.total, 0) AS abonado,
                     GREATEST(COALESCE(ab.monto, $3::numeric) - COALESCE(ab.total, 0), 0) AS saldo
              FROM periodos pe
              LEFT JOIN abonado ab ON ab.periodo = pe.periodo
              WHERE GREATEST(COALESCE(ab.monto, $3::numeric) - COALESCE(ab.total, 0), 0) > 0.005
              ORDER BY pe.periodo ASC",
            inquilinoId, fechaInicioTxt, renta)).Rows;

    /// <summary>Asegura la fila en pagos para el periodo y la bloquea FOR UPDATE dentro de la transacción.</summary>
    public static async Task<Row> AsegurarPagoEnTx(DbTx tx, string inquilinoId, string periodo, decimal renta, string metodo)
    {
        await tx.Query(
            @"INSERT INTO pagos (inquilino_id, periodo, monto, metodo)
              VALUES ($1, $2, $3, $4)
              ON CONFLICT (inquilino_id, periodo) DO NOTHING",
            inquilinoId, periodo, renta, metodo);
        return (await tx.Query(
            "SELECT * FROM pagos WHERE inquilino_id = $1 AND periodo = $2 FOR UPDATE",
            inquilinoId, periodo)).First!;
    }

    /// <summary>El abono existe y es de un inquilino del propietario.</summary>
    public static async Task<bool> AbonoDelPropietario(string abonoId, string adminId) =>
        (await Db.Query(
            @"SELECT a.id FROM abonos_deposito a
              JOIN inquilinos i ON i.id = a.inquilino_id
              WHERE a.id = $1 AND i.admin_id = $2", abonoId, adminId)).First != null;

    public static async Task<Row?> Actualizar(string abonoId, JsonNode? monto, JsonNode? fecha, JsonNode? metodo, JsonNode? nota) =>
        (await Db.Query(
            @"UPDATE abonos_deposito SET
                monto  = COALESCE($2, monto),
                fecha  = COALESCE($3, fecha),
                metodo = COALESCE($4, metodo),
                nota   = COALESCE($5, nota)
              WHERE id = $1 RETURNING *", abonoId, monto, fecha, metodo, nota)).First;

    public static Task Eliminar(string abonoId) => Db.Query("DELETE FROM abonos_deposito WHERE id = $1", abonoId);
}
