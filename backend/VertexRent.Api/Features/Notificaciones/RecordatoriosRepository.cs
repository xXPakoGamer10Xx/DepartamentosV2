namespace VertexRent.Api;

/// <summary>Todo el SQL de los recordatorios programados. Sin reglas de negocio.</summary>
internal static class RecordatoriosRepository
{
    public static async Task<List<Row>> ConfigRecordatoriosRenta() =>
        (await Db.Query("SELECT admin_id, valor FROM configuracion WHERE clave = 'recordatorios_renta_dias'")).Rows;

    /// <summary>¿Ya se envió hoy un aviso con esta clave a este usuario?</summary>
    public static async Task<bool> YaNotificadoHoy(string usuarioId, string tipo, string dedupeKey) =>
        (await Db.Query(
            @"SELECT 1 FROM notificaciones
              WHERE usuario_id = $1 AND tipo = $2 AND dedupe_key = $3
                AND DATE(created_at) = CURRENT_DATE
              LIMIT 1", usuarioId, tipo, dedupeKey)).RowCount > 0;

    public static async Task<List<Row>> InquilinosActivosParaRenta() =>
        (await Db.Query(
            @"SELECT i.id, i.usuario_id, i.fecha_pago, i.nombre_completo, i.depto_numero, i.admin_id
              FROM inquilinos i WHERE i.estado = 'activo'")).Rows;

    /// <summary>Contratos activos que vencen entre hoy y dentro de <paramref name="dias"/> días.</summary>
    public static async Task<List<Row>> ContratosPorVencer(int dias) =>
        (await Db.Query(
            @"SELECT i.id, i.usuario_id, i.fecha_termino, i.nombre_completo, i.depto_numero, i.admin_id
              FROM inquilinos i
              WHERE i.estado = 'activo'
                AND i.fecha_termino <= CURRENT_DATE + make_interval(days => $1::int)
                AND i.fecha_termino >= CURRENT_DATE", dias)).Rows;

    public static async Task<List<Row>> InquilinosConDepositoDiferido() =>
        (await Db.Query(
            @"SELECT i.id, i.usuario_id, i.deposito_fechas, i.nombre_completo, i.depto_numero, i.admin_id
              FROM inquilinos i WHERE i.estado = 'activo'")).Rows;

    public static async Task<List<Row>> PromesasPendientes() =>
        (await Db.Query(
            @"SELECT p.id, p.fecha_promesa, p.periodo, i.admin_id, i.nombre_completo, i.depto_numero
              FROM pagos p JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE p.confirmado = false AND p.fecha_promesa IS NOT NULL")).Rows;
}
