namespace VertexRent.Api;

/// <summary>Todo el SQL de los reportes de ingresos. Sin reglas de negocio ni HTTP.</summary>
internal static class ReportesRepository
{
    // El ingreso se contabiliza por la FECHA en que entró el dinero (abonos y cuotas pagadas), no por el periodo que cubren.
    public static async Task<decimal> TotalRenta(string adminId, int year) =>
        (await Db.Query(
            @"SELECT COALESCE(SUM(a.monto), 0) AS total
              FROM abonos_pago a
              JOIN pagos p ON p.id = a.pago_id
              JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE i.admin_id = $1 AND EXTRACT(YEAR FROM a.fecha) = $2", adminId, year)).First!.D("total");

    public static async Task<decimal> TotalExtra(string adminId, int year) =>
        (await Db.Query(
            @"SELECT COALESCE(SUM(c.monto), 0) AS total
              FROM cuotas_extra c
              JOIN inquilinos i ON i.id = c.inquilino_id
              WHERE i.admin_id = $1 AND c.estado = 'pagado' AND EXTRACT(YEAR FROM c.pagado_en) = $2", adminId, year)).First!.D("total");

    public static async Task<decimal> TotalDeposito(string adminId, int year) =>
        (await Db.Query(
            @"SELECT COALESCE(SUM(ad.monto), 0) AS total
              FROM abonos_deposito ad
              JOIN inquilinos i ON i.id = ad.inquilino_id
              WHERE i.admin_id = $1 AND EXTRACT(YEAR FROM ad.fecha) = $2", adminId, year)).First!.D("total");

    /// <summary>Ingresos del año por departamento, mes y tipo (renta / extra / deposito).</summary>
    public static async Task<List<Row>> DetalleMensual(string adminId, int year) =>
        (await Db.Query(
            @"WITH ingresos AS (
                SELECT i.depto_numero, EXTRACT(MONTH FROM a.fecha)::int AS mes, 'renta' AS tipo, a.monto
                FROM abonos_pago a
                JOIN pagos p ON p.id = a.pago_id
                JOIN inquilinos i ON i.id = p.inquilino_id
                WHERE i.admin_id = $1 AND EXTRACT(YEAR FROM a.fecha) = $2
                UNION ALL
                SELECT i.depto_numero, EXTRACT(MONTH FROM c.pagado_en)::int, 'extra', c.monto
                FROM cuotas_extra c
                JOIN inquilinos i ON i.id = c.inquilino_id
                WHERE i.admin_id = $1 AND c.estado = 'pagado' AND EXTRACT(YEAR FROM c.pagado_en) = $2
                UNION ALL
                SELECT i.depto_numero, EXTRACT(MONTH FROM ad.fecha)::int, 'deposito', ad.monto
                FROM abonos_deposito ad
                JOIN inquilinos i ON i.id = ad.inquilino_id
                WHERE i.admin_id = $1 AND EXTRACT(YEAR FROM ad.fecha) = $2
              )
              SELECT depto_numero, mes, tipo, SUM(monto) AS total
              FROM ingresos
              GROUP BY depto_numero, mes, tipo", adminId, year)).Rows;

    public static async Task<List<int>> AniosConIngresos(string adminId) =>
        (await Db.Query(
            @"SELECT DISTINCT y FROM (
                SELECT EXTRACT(YEAR FROM a.fecha)::int AS y
                FROM abonos_pago a JOIN pagos p ON p.id = a.pago_id JOIN inquilinos i ON i.id = p.inquilino_id
                WHERE i.admin_id = $1
                UNION
                SELECT EXTRACT(YEAR FROM ad.fecha)::int
                FROM abonos_deposito ad JOIN inquilinos i ON i.id = ad.inquilino_id
                WHERE i.admin_id = $1
                UNION
                SELECT EXTRACT(YEAR FROM c.pagado_en)::int
                FROM cuotas_extra c JOIN inquilinos i ON i.id = c.inquilino_id
                WHERE i.admin_id = $1 AND c.pagado_en IS NOT NULL
              ) t WHERE y IS NOT NULL ORDER BY y DESC", adminId)).Rows.Select(r => r.I("y")).ToList();
}
