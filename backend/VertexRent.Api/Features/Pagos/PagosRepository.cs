using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Todo el SQL del módulo de pagos. Sin reglas de negocio ni HTTP.</summary>
internal static class PagosRepository
{
    // ── Inquilinos ───────────────────────────────────────────────────────────
    public static async Task<Row?> InquilinoActivoDelUsuario(string inquilinoId, string usuarioId) =>
        (await Db.Query(
            "SELECT id, nombre_completo, depto_numero, renta, metodo_pago FROM inquilinos WHERE id = $1 AND usuario_id = $2 AND estado = 'activo'",
            inquilinoId, usuarioId)).First;

    public static async Task<Row> DatosParaAviso(string inquilinoId) =>
        (await Db.Query("SELECT nombre_completo, depto_numero, admin_id FROM inquilinos WHERE id = $1", inquilinoId)).First!;

    public static async Task<Row> RentaYMetodo(string inquilinoId) =>
        (await Db.Query("SELECT renta, metodo_pago FROM inquilinos WHERE id = $1", inquilinoId)).First!;

    public static async Task<string?> UsuarioDelInquilino(string inquilinoId) =>
        (await Db.Query("SELECT usuario_id FROM inquilinos WHERE id = $1", inquilinoId)).First?.S("usuario_id");

    // ── Pagos ────────────────────────────────────────────────────────────────
    public static async Task<Row?> PagoDe(string inquilinoId, string periodo) =>
        (await Db.Query("SELECT * FROM pagos WHERE inquilino_id = $1 AND periodo = $2", inquilinoId, periodo)).First;

    /// <summary>Inserta el pago del periodo si no existe y devuelve el vigente (seguro ante peticiones simultáneas).</summary>
    public static async Task<Row> CrearPagoSiNoExiste(string inquilinoId, string periodo, decimal monto, string? metodo, string? qrToken = null)
    {
        var ins = await Db.Query(
            @"INSERT INTO pagos (inquilino_id, periodo, monto, metodo, qr_token) VALUES ($1, $2, $3, $4, $5)
              ON CONFLICT (inquilino_id, periodo) DO NOTHING RETURNING *",
            inquilinoId, periodo, monto, metodo, qrToken);
        return ins.First ?? (await PagoDe(inquilinoId, periodo))!;
    }

    public static async Task<Row?> AsignarQr(string pagoId, string token, decimal? monto = null) =>
        monto is { } m
            ? (await Db.Query("UPDATE pagos SET qr_token = $1, monto = $2 WHERE id = $3 RETURNING *", token, m, pagoId)).First
            : (await Db.Query("UPDATE pagos SET qr_token = $1 WHERE id = $2 AND qr_token IS NULL RETURNING *", token, pagoId)).First;

    public static async Task<Row?> RefrescarMonto(string pagoId, decimal monto) =>
        (await Db.Query("UPDATE pagos SET monto = $1 WHERE id = $2 AND confirmado = false RETURNING *", monto, pagoId)).First;

    public static async Task<Row?> InfoPorToken(string token, string adminId) =>
        (await Db.Query(
            @"SELECT p.id, p.periodo, p.monto, p.confirmado, i.nombre_completo, i.depto_numero, i.renta
              FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE p.qr_token = $1 AND i.admin_id = $2", token, adminId)).First;

    public static async Task<Row?> PagoPorToken(string token, string adminId) =>
        (await Db.Query(
            @"SELECT p.*, i.nombre_completo, i.depto_numero, i.admin_id, i.usuario_id AS inquilino_usuario_id
              FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE p.qr_token = $1 AND i.admin_id = $2", token, adminId)).First;

    public static async Task<Row?> PagoDelPropietario(string pagoId, string adminId) =>
        (await Db.Query(
            @"SELECT p.*, i.nombre_completo, i.depto_numero, i.usuario_id AS inquilino_usuario_id, i.admin_id
              FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE p.id = $1 AND i.admin_id = $2", pagoId, adminId)).First;

    /// <summary>La imagen del comprobante de un pago (null si no tiene).</summary>
    public static async Task<string?> ImagenDelComprobante(string pagoId) =>
        (await Db.Query("SELECT comprobante_url FROM pagos WHERE id = $1", pagoId)).First?.S("comprobante_url");

    public static Task MarcarEscaneo(string pagoId, string? actorId) =>
        Db.Query("UPDATE pagos SET escaneado_por = $2, escaneado_en = NOW() WHERE id = $1", pagoId, actorId);

    public static async Task<Row> Rechazar(string pagoId, string? comentario) =>
        (await Db.Query(
            "UPDATE pagos SET rechazado = true, rechazado_en = NOW(), comentario_admin = $2 WHERE id = $1 RETURNING *",
            pagoId, comentario)).First!;

    public static async Task<Row?> GuardarComprobante(string pagoId, string imagen) =>
        (await Db.Query(
            @"UPDATE pagos SET comprobante_url = $1, comprobante_subido_en = NOW(),
                rechazado = false, rechazado_en = NULL, comentario_admin = NULL
              WHERE id = $2 AND confirmado = false RETURNING *", imagen, pagoId)).First;

    public static async Task<List<Row>> ComprobantesPendientes(string adminId) =>
        (await Db.Query(
            @"SELECT p.id, p.monto, p.periodo, p.comprobante_url, p.comprobante_subido_en,
                     i.nombre_completo, i.depto_numero, i.id AS inquilino_id
              FROM pagos p
              JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE p.comprobante_url IS NOT NULL AND p.confirmado = false AND p.rechazado = false
                AND i.admin_id = $1
              ORDER BY p.comprobante_subido_en DESC", adminId)).Rows;

    public static async Task<List<Row>> Historial(string inquilinoId) =>
        (await Db.Query(
            @"SELECT p.id, p.inquilino_id, p.periodo, p.monto, p.metodo, p.qr_token, p.confirmado, p.confirmado_en,
                     p.escaneado_por, p.escaneado_en, p.comprobante_subido_en, p.rechazado, p.rechazado_en, p.comentario_admin,
                     p.created_at, p.updated_at, p.fecha_promesa,
                     (p.comprobante_url IS NOT NULL) AS tiene_comprobante,
                     i.fecha_pago, i.nombre_completo, i.depto_numero,
                     u.nombre_completo AS escaneado_por_nombre,
                     COALESCE((SELECT c.valor FROM configuracion c
                               WHERE c.admin_id = i.admin_id AND c.clave = 'dias_gracia_retraso'), '0') AS dias_gracia
              FROM pagos p
              JOIN inquilinos i ON i.id = p.inquilino_id
              LEFT JOIN usuarios u ON u.id = p.escaneado_por
              WHERE p.inquilino_id = $1
              ORDER BY p.periodo DESC", inquilinoId)).Rows;

    public static async Task<Row?> PagoParaCancelar(string pagoId, string adminId) =>
        (await Db.Query(
            @"SELECT p.id, p.inquilino_id, p.periodo, p.confirmado_en, p.created_at, i.usuario_id
              FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE p.id = $1 AND i.admin_id = $2", pagoId, adminId)).First;

    /// <summary>abonos_pago y promesas_pago se borran en cascada; las cuotas extra son independientes.</summary>
    public static Task EliminarPago(string pagoId) => Db.Query("DELETE FROM pagos WHERE id = $1", pagoId);

    public static async Task<List<Row>> EstadosActuales(string periodo, string adminId) =>
        (await Db.Query(
            @"SELECT i.id AS inquilino_id, p.id AS pago_id, p.monto,
                     COALESCE(p.confirmado, false) AS confirmado, p.confirmado_en,
                     COALESCE(p.rechazado, false) AS rechazado, COALESCE(p.comprobante_url IS NOT NULL, false) AS tiene_comprobante,
                     COALESCE(ab.total_abonado, 0) AS total_abonado
              FROM inquilinos i
              LEFT JOIN pagos p ON p.inquilino_id = i.id AND p.periodo = $1
              LEFT JOIN LATERAL (SELECT SUM(monto) AS total_abonado FROM abonos_pago WHERE pago_id = p.id) ab ON true
              WHERE i.estado = 'activo' AND i.admin_id = $2", periodo, adminId)).Rows;

    public static async Task<List<Row>> CuotasDelPeriodo(string inquilinoId, string periodo) =>
        (await Db.Query(
            @"SELECT id, concepto, monto, estado, created_at, pagado_en FROM cuotas_extra
              WHERE inquilino_id = $1 AND (estado = 'pendiente' OR (estado = 'pagado' AND periodo = $2))
              ORDER BY created_at", inquilinoId, periodo)).Rows;

    public static async Task<Row> ResumenCuotasPendientes(string inquilinoId) =>
        (await Db.Query(
            @"SELECT COALESCE(SUM(monto), 0) AS total_extra,
                     json_agg(json_build_object('id', id, 'concepto', concepto, 'monto', monto)) FILTER (WHERE id IS NOT NULL) AS cuotas
              FROM cuotas_extra WHERE inquilino_id = $1 AND estado = 'pendiente'", inquilinoId)).First!;

    // ── Abonos ───────────────────────────────────────────────────────────────
    public static async Task<decimal> TotalAbonado(string pagoId) =>
        (await Db.Query("SELECT COALESCE(SUM(monto), 0) AS total FROM abonos_pago WHERE pago_id = $1", pagoId)).First!.D("total");

    public static async Task<bool> PagoAccesible(string pagoId, AuthUser u)
    {
        var filtro = u.Rol == "inquilino" ? "AND i.usuario_id = $2" : "AND i.admin_id = $2";
        return (await Db.Query($"SELECT p.id FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id WHERE p.id = $1 {filtro}", pagoId, u.Id)).First != null;
    }

    public static async Task<List<Row>> Abonos(string pagoId) =>
        (await Db.Query(
            @"SELECT a.id, a.pago_id, a.monto, a.fecha, a.metodo, a.nota, a.registrado_por, a.created_at,
                     (a.comprobante_url IS NOT NULL) AS tiene_comprobante,
                     u.nombre_completo AS registrado_por_nombre
              FROM abonos_pago a
              LEFT JOIN usuarios u ON u.id = a.registrado_por
              WHERE a.pago_id = $1
              ORDER BY a.fecha ASC, a.created_at ASC", pagoId)).Rows;

    /// <summary>Id del pago al que pertenece el abono, si el abono es de un inquilino del propietario.</summary>
    public static async Task<string?> PagoDelAbono(string abonoId, string adminId) =>
        (await Db.Query(
            @"SELECT a.pago_id FROM abonos_pago a
              JOIN pagos p ON p.id = a.pago_id
              JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE a.id = $1 AND i.admin_id = $2", abonoId, adminId)).First?.S("pago_id");

    public static async Task<Row?> ActualizarAbono(string abonoId, JsonNode? monto, JsonNode? fecha, JsonNode? metodo, JsonNode? nota) =>
        (await Db.Query(
            @"UPDATE abonos_pago SET
                monto  = COALESCE($2, monto),
                fecha  = COALESCE($3, fecha),
                metodo = COALESCE($4, metodo),
                nota   = COALESCE($5, nota)
              WHERE id = $1 RETURNING *", abonoId, monto, fecha, metodo, nota)).First;

    public static Task EliminarAbono(string abonoId) => Db.Query("DELETE FROM abonos_pago WHERE id = $1", abonoId);

    // ── Promesas de pago ─────────────────────────────────────────────────────
    public static async Task<bool> PagoDelPropietarioExiste(string pagoId, string adminId) =>
        (await Db.Query("SELECT p.id FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id WHERE p.id = $1 AND i.admin_id = $2", pagoId, adminId)).First != null;

    public static async Task<Row?> GuardarPromesa(string pagoId, string? fecha, string actorId)
    {
        var updated = await Db.Query("UPDATE pagos SET fecha_promesa = $2 WHERE id = $1 RETURNING *", pagoId, fecha);
        // Cada vez que se guarda una fecha (no al borrarla) se agrega al historial.
        if (fecha != null)
            await Db.Query("INSERT INTO promesas_pago (pago_id, fecha_promesa, creado_por) VALUES ($1, $2, $3)", pagoId, fecha, actorId);
        return updated.First;
    }

    public static async Task<List<Row>> Promesas(string inquilinoId, AuthUser u)
    {
        var filtro = u.Rol == "inquilino" ? "AND i.usuario_id = $2" : "AND i.admin_id = $2";
        return (await Db.Query(
            $@"SELECT pp.id, pp.fecha_promesa, pp.created_at, p.periodo, p.monto, p.confirmado, p.confirmado_en
               FROM promesas_pago pp
               JOIN pagos p ON p.id = pp.pago_id
               JOIN inquilinos i ON i.id = p.inquilino_id
               WHERE p.inquilino_id = $1 {filtro}
               ORDER BY pp.created_at DESC", inquilinoId, u.Id)).Rows;
    }

    public static async Task<bool> EliminarPromesa(string id, string adminId) =>
        (await Db.Query(
            @"DELETE FROM promesas_pago pp
              USING pagos p, inquilinos i
              WHERE pp.id = $1 AND pp.pago_id = p.id AND p.inquilino_id = i.id AND i.admin_id = $2
              RETURNING pp.id", id, adminId)).First != null;

    // ── Deuda (saldos) ───────────────────────────────────────────────────────
    // Fecha límite del mes `gs`: primer día + (día pactado - 1) + días de gracia. Un mes solo cuenta
    // como DEUDA VENCIDA cuando CURRENT_DATE ya pasó esa fecha.
    const string MesVencido = @"CURRENT_DATE > (
  gs::date
  + (COALESCE(NULLIF(regexp_replace(i.fecha_pago, '[^0-9]', '', 'g'), '')::int, 1) - 1)
  + COALESCE((SELECT c.valor FROM configuracion c
              WHERE c.admin_id = i.admin_id AND c.clave = 'dias_gracia_retraso')::int, 0)
)";
    const string DeudaMes = "i.renta - COALESCE(ab.total, 0)"; // solo renta; cuotas extra van aparte (deuda_cuotas_extra)
    const string DeudaVencida = "COALESCE(SUM(GREATEST(CASE WHEN " + MesVencido + " THEN " + DeudaMes + " ELSE 0 END, 0)), 0)";
    const string DeudaTotal = "COALESCE(SUM(GREATEST(" + DeudaMes + ", 0)), 0)";
    const string PeriodoVencido = "MIN(CASE WHEN " + MesVencido + " AND " + DeudaMes + " > 0.5 THEN to_char(gs, 'YYYY-MM') END)";

    const string SaldosFrom = @"
FROM inquilinos i
CROSS JOIN LATERAL generate_series(
  date_trunc('month', i.fecha_inicio), date_trunc('month', CURRENT_DATE), interval '1 month'
) AS gs
LEFT JOIN LATERAL (
  SELECT SUM(a.monto) AS total FROM pagos p JOIN abonos_pago a ON a.pago_id = p.id
  WHERE p.inquilino_id = i.id AND p.periodo = to_char(gs, 'YYYY-MM')
) ab ON true";

    const string ExtraCols = @"COALESCE((SELECT SUM(monto) FROM cuotas_extra WHERE inquilino_id = i.id AND estado = 'pendiente'), 0) AS deuda_cuotas_extra,
GREATEST(MAX(i.deposito) - COALESCE((SELECT SUM(monto) FROM abonos_deposito WHERE inquilino_id = i.id), 0), 0) AS deuda_deposito";

    public static async Task<List<Row>> Saldos(string adminId) =>
        (await Db.Query(
            $@"SELECT i.id AS inquilino_id, i.depto_numero, i.nombre_completo,
                      {DeudaVencida} AS deuda_vencida,
                      {DeudaTotal} AS deuda_total,
                      {PeriodoVencido} AS periodo_vencido,
                      {ExtraCols}
               {SaldosFrom}
               WHERE i.estado = 'activo' AND i.admin_id = $1
               GROUP BY i.id", adminId)).Rows;

    public static async Task<List<Row>> ResumenSaldos(string adminId) =>
        (await Db.Query(
            $@"SELECT * FROM (
                 SELECT i.id AS inquilino_id, i.depto_numero, i.nombre_completo,
                        {DeudaVencida} AS deuda_vencida,
                        {DeudaTotal} AS deuda_total,
                        {ExtraCols}
                 {SaldosFrom}
                 WHERE i.estado = 'activo' AND i.admin_id = $1
                 GROUP BY i.id
               ) sub", adminId)).Rows;

    public static async Task<List<Row>> SaldoDetalle(string inquilinoId, string? fechaInicio, string? renta) =>
        (await Db.Query(
            @"WITH periodos AS (
                SELECT to_char(gs, 'YYYY-MM') AS periodo
                FROM generate_series(
                  date_trunc('month', $2::date), date_trunc('month', CURRENT_DATE), interval '1 month'
                ) AS gs
              ),
              abonado AS (
                SELECT p.periodo, COALESCE(SUM(a.monto), 0) AS total
                FROM pagos p LEFT JOIN abonos_pago a ON a.pago_id = p.id
                WHERE p.inquilino_id = $1 GROUP BY p.periodo
              )
              SELECT pe.periodo,
                     $3::numeric AS monto_esperado,
                     COALESCE(ab.total, 0) AS abonado,
                     GREATEST($3::numeric - COALESCE(ab.total, 0), 0) AS saldo
              FROM periodos pe
              LEFT JOIN abonado ab ON ab.periodo = pe.periodo
              ORDER BY pe.periodo", inquilinoId, fechaInicio, renta)).Rows;
}
