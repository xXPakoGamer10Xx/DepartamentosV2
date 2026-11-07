namespace VertexRent.Api;

public sealed record ContratoResultado(byte[] Docx, byte[] Pdf, bool Aproximado);

/// <summary>Rellena el Word de la plantilla y lo convierte a PDF fiel al original.</summary>
public static class ContratoRenderer
{
    /// <param name="data">Fila del inquilino + datos de contexto (ver ContratoVariables.Build).</param>
    /// <param name="extra">Valores de campos personalizados de la plantilla (clave → valor).</param>
    public static async Task<ContratoResultado> Generar(byte[] plantilla, IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, string>? extra = null)
    {
        var data2 = new ContractData(data);
        var vars = ContratoVariables.Build(data2);
        if (extra != null) foreach (var (k, v) in extra) vars[k] = v ?? "";

        var docx = DocxService.FillTemplate(plantilla, vars, pruneEmpty: true);
        // El título del documento es lo que muestra el visor de PDF en la pestaña.
        docx = DocxService.PonerTitulo(docx, $"Contrato – {data2.Str("nombre_completo")}");
        if (LibreOfficeService.Disponible)
            return new ContratoResultado(docx, await LibreOfficeService.DocxAPdf(docx), false);

        // Sin LibreOffice (p. ej. desarrollo): aproximación DOCX → HTML → QuestPDF (pierde tablas e imágenes).
        var paragraphs = PdfService.ParseHtml(PdfService.PruneEmptySections(DocxService.ToHtml(docx)));
        var pdf = await Task.Run(() => ContratoPersonalizadoTemplate.Render(paragraphs, data2.Str("nombre_completo"), data2.Str("arrendador_nombre")));
        return new ContratoResultado(docx, pdf, true);
    }
}
