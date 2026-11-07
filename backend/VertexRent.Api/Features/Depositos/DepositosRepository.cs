using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Todo el SQL del módulo de depósitos. Sin reglas de negocio ni HTTP.</summary>
internal static class DepositosRepository
{
    public static async Task<decimal> TotalPagado(string inquilinoId) =>
        (await Db.Query("SELECT COALESCE(SUM(monto), 0) AS pagado FROM abonos_deposito WHERE inquilino_id = $1", inquilinoId)).First!.D("pagado");

    public static async Task<List<Row>> Abonos(string inquilinoId) =>
        (await Db.Query(
            @"SELECT a.*, u.nombre_completo AS registrado_por_nombre
              FROM abonos_deposito a
              LEFT JOIN usuarios u ON u.id = a.registrado_por
              WHERE a.inquilino_id = $1
              ORDER BY a.fecha ASC, a.created_at ASC", inquilinoId)).Rows;

    public static async Task<Row> Insertar(string inquilinoId, decimal monto, string? fecha, string? metodo, string? nota, string? comprobante, string? actorId) =>
        (await Db.Query(
            @"INSERT INTO abonos_deposito (inquilino_id, monto, fecha, metodo, nota, comprobante_url, registrado_por)
              VALUES ($1, $2, COALESCE($3, CURRENT_DATE), $4, $5, $6, $7) RETURNING *",
            inquilinoId, monto, fecha, metodo, nota, comprobante, actorId)).First!;

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
