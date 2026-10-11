namespace VertexRent.Api;

/// <summary>
/// Migración única (idempotente) del modelo anterior de contratos al de plantillas Word:
/// la plantilla global (config) y los contratos editados por inquilino pasan a ser filas de plantillas_contrato.
/// Cada origen se borra al migrarse, así que repetir el arranque no duplica nada.
/// </summary>
public static class ContratoMigracion
{
    public static async Task Run()
    {
        try
        {
            await MigrarGlobales();
            await MigrarPorInquilino();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"⚠️  Migración de contratos: {ex.Message}");
        }
    }

    static async Task MigrarGlobales()
    {
        var r = await Db.Query(
            "SELECT admin_id, clave, valor FROM configuracion WHERE clave IN ('contrato_docx_template','contrato_html_template','contrato_docx_nombre')");
        foreach (var grupo in r.Rows.GroupBy(x => x.S("admin_id")!))
        {
            var adminId = grupo.Key;
            string? Val(string k) => grupo.FirstOrDefault(x => x.S("clave") == k)?.S("valor");
            try
            {
                byte[]? docx = null; var nombre = "Mi plantilla anterior";
                if (!string.IsNullOrEmpty(Val("contrato_docx_template")))
                {
                    docx = Convert.FromBase64String(Val("contrato_docx_template")!);
                    var n = Val("contrato_docx_nombre");
                    if (!string.IsNullOrWhiteSpace(n)) nombre = Path.GetFileNameWithoutExtension(n);
                }
                else if (!string.IsNullOrEmpty(Val("contrato_html_template")))
                    docx = await DocxService.FromHtml(Val("contrato_html_template")!);

                if (docx != null)
                    await Db.Query(
                        @"INSERT INTO plantillas_contrato (admin_id, nombre, docx_original, docx_plantilla, estado, predeterminada)
                          VALUES ($1, $2, $3, $3, 'lista', NOT EXISTS (SELECT 1 FROM plantillas_contrato WHERE admin_id = $1 AND predeterminada))",
                        adminId, nombre.Length > 120 ? nombre[..120] : nombre, docx);
                await Db.Query(
                    "DELETE FROM configuracion WHERE admin_id = $1 AND clave IN ('contrato_docx_template','contrato_html_template','contrato_docx_nombre')", adminId);
            }
            catch (Exception ex) { Console.Error.WriteLine($"⚠️  No se migró la plantilla del admin {adminId}: {ex.Message}"); }
        }
    }

    static async Task MigrarPorInquilino()
    {
        var r = await Db.Query("SELECT id, admin_id, nombre_completo, contrato_html FROM inquilinos WHERE contrato_html IS NOT NULL AND contrato_html <> ''");
        foreach (var inq in r.Rows)
        {
            try
            {
                var docx = await DocxService.FromHtml(inq.S("contrato_html")!);
                var nombre = $"Personalizado – {inq.S("nombre_completo")}";
                var ins = await Db.Query(
                    @"INSERT INTO plantillas_contrato (admin_id, nombre, docx_original, docx_plantilla, estado)
                      VALUES ($1, $2, $3, $3, 'lista') RETURNING id", inq.S("admin_id"), nombre.Length > 120 ? nombre[..120] : nombre, docx);
                await Db.Query("UPDATE inquilinos SET plantilla_id = $2, contrato_html = NULL WHERE id = $1", inq.S("id"), ins.First!.S("id"));
            }
            catch (Exception ex) { Console.Error.WriteLine($"⚠️  No se migró el contrato del inquilino {inq.S("id")}: {ex.Message}"); }
        }
    }
}
