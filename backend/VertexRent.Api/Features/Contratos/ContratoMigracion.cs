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
        foreach (var grupo in (await ContratoMigracionRepository.ConfigAnterior()).GroupBy(x => x.S("admin_id")!))
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
                    await ContratoMigracionRepository.InsertarPlantillaGlobal(adminId, nombre.Length > 120 ? nombre[..120] : nombre, docx);
                await ContratoMigracionRepository.BorrarConfigAnterior(adminId);
            }
            catch (Exception ex) { Console.Error.WriteLine($"⚠️  No se migró la plantilla del admin {adminId}: {ex.Message}"); }
        }
    }

    static async Task MigrarPorInquilino()
    {
        foreach (var inq in await ContratoMigracionRepository.InquilinosConContratoHtml())
        {
            try
            {
                var docx = await DocxService.FromHtml(inq.S("contrato_html")!);
                var nombre = $"Personalizado – {inq.S("nombre_completo")}";
                var plantillaId = await ContratoMigracionRepository.InsertarPlantillaDeInquilino(inq.S("admin_id")!, nombre.Length > 120 ? nombre[..120] : nombre, docx);
                await ContratoMigracionRepository.AsignarPlantillaAInquilino(inq.S("id")!, plantillaId);
            }
            catch (Exception ex) { Console.Error.WriteLine($"⚠️  No se migró el contrato del inquilino {inq.S("id")}: {ex.Message}"); }
        }
    }
}
