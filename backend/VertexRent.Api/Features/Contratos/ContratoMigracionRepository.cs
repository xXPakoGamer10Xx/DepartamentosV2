namespace VertexRent.Api;

/// <summary>SQL de la migración única del modelo anterior de contratos. Sin reglas de negocio.</summary>
internal static class ContratoMigracionRepository
{
    const string ClavesAnteriores = "'contrato_docx_template','contrato_html_template','contrato_docx_nombre'";

    public static async Task<List<Row>> ConfigAnterior() =>
        (await Db.Query($"SELECT admin_id, clave, valor FROM configuracion WHERE clave IN ({ClavesAnteriores})")).Rows;

    /// <summary>Crea la plantilla migrada; es la predeterminada solo si el admin aún no tiene una.</summary>
    public static Task InsertarPlantillaGlobal(string adminId, string nombre, byte[] docx) =>
        Db.Query(
            @"INSERT INTO plantillas_contrato (admin_id, nombre, docx_original, docx_plantilla, estado, predeterminada)
              VALUES ($1, $2, $3, $3, 'lista', NOT EXISTS (SELECT 1 FROM plantillas_contrato WHERE admin_id = $1 AND predeterminada))",
            adminId, nombre, docx);

    public static Task BorrarConfigAnterior(string adminId) =>
        Db.Query($"DELETE FROM configuracion WHERE admin_id = $1 AND clave IN ({ClavesAnteriores})", adminId);

    public static async Task<List<Row>> InquilinosConContratoHtml() =>
        (await Db.Query("SELECT id, admin_id, nombre_completo, contrato_html FROM inquilinos WHERE contrato_html IS NOT NULL AND contrato_html <> ''")).Rows;

    public static async Task<string> InsertarPlantillaDeInquilino(string adminId, string nombre, byte[] docx) =>
        (await Db.Query(
            @"INSERT INTO plantillas_contrato (admin_id, nombre, docx_original, docx_plantilla, estado)
              VALUES ($1, $2, $3, $3, 'lista') RETURNING id", adminId, nombre, docx)).First!.S("id")!;

    public static Task AsignarPlantillaAInquilino(string inquilinoId, string plantillaId) =>
        Db.Query("UPDATE inquilinos SET plantilla_id = $2, contrato_html = NULL WHERE id = $1", inquilinoId, plantillaId);
}
