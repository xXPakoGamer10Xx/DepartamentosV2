using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace VertexRent.Api;

public sealed record PdfConvertido(byte[] Docx, string? Aviso);

/// <summary>
/// Convierte un contrato en PDF a Word. Un PDF no guarda párrafos ni tablas, solo texto colocado en la página, así que
/// la IA lo transcribe a bloques (párrafos, tablas de firmas) y de ahí se construye el .docx. El texto debe salir
/// IGUAL: se compara contra el texto real del PDF y, si hay diferencias, se avisa para revisarlas.
/// </summary>
public static class PdfImportService
{
    public static bool EsPdf(byte[] b) => b.Length > 4 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46;

    const string Prompt = """
Eres un transcriptor de contratos. Recibes un contrato en PDF y debes reconstruir su ESTRUCTURA para armar un documento de Word.

REGLAS (muy importantes, es un documento legal):
1. Copia el texto EXACTAMENTE como aparece: no corrijas, no resumas, no reordenes, no traduzcas. Si una palabra viene cortada con guion al final de una línea (ej. "formal-" / "izan"), únela ("formalizan").
2. Cada párrafo del documento es UN bloque (no un bloque por línea visual). Respeta títulos centrados y la alineación. La negrita y la cursiva SOLO donde realmente se ven en el PDF, fragmento por fragmento (por ejemplo, solo el nombre o el monto, no la frase completa que lo rodea). Si el párrafo está en negrita solo en parte, separa los "runs".
3. Bloques que están lado a lado en la página (por ejemplo firmas en dos columnas) van en una tabla sin bordes.
4. Una raya horizontal sobre un nombre (línea de firma) se indica con "linea_arriba": true en ese párrafo.
5. Si en el original hay un salto de página explícito antes de un bloque, usa {"tipo":"salto_pagina"}. No inventes saltos.
6. Ignora números de página y encabezados repetidos en cada hoja.

Responde SOLO con JSON con esta forma:
{"bloques":[
 {"tipo":"parrafo","alineacion":"izquierda|centro|derecha|justificado","tamano":"pequeno|normal|grande","espacio_antes":"normal|grande","linea_arriba":false,
  "runs":[{"texto":"...","negrita":false,"cursiva":false}]},
 {"tipo":"tabla","bordes":false,"filas":[[ [ {parrafo}, {parrafo} ], [ {parrafo} ] ]]},
 {"tipo":"salto_pagina"}
]}
En "filas", cada fila es una lista de celdas y cada celda es una lista de párrafos (mismo formato que "parrafo", sin "tipo").
""";

    public static async Task<PdfConvertido> ConvertirADocx(byte[] pdf)
    {
        var key = GeminiService.RequireKey("Para subir un PDF se necesita la IA, que no está configurada en el servidor. Sube tu contrato en Word (.docx).");
        var raw = (await GeminiService.Generate(key, Prompt, ("application/pdf", Convert.ToBase64String(pdf)), respuestaJson: true, pensamientoMinimo: true)).Trim();
        var ini = raw.IndexOf('{'); var fin = raw.LastIndexOf('}');
        JsonArray? bloques = null;
        try { if (ini >= 0 && fin > ini) bloques = JsonNode.Parse(raw[ini..(fin + 1)])?["bloques"] as JsonArray; } catch { }
        if (bloques == null || bloques.Count == 0)
            throw new AppError("No se pudo leer el PDF. Intenta con una versión en Word (.docx) o con un PDF de mejor calidad.", 422);

        var docx = Construir(bloques);
        return new PdfConvertido(docx, await Verificar(pdf, docx));
    }

    // ── Comparación del texto: lo que dice el PDF vs. lo que quedó en el Word ────────────────
    static string[] Palabras(string t) =>
        Regex.Matches(t.ToLowerInvariant().Replace("-\n", "").Replace("-\r\n", ""), @"[\p{L}\p{N}]+").Select(m => m.Value).ToArray();

    static async Task<string?> Verificar(byte[] pdf, byte[] docx)
    {
        try
        {
            var textoPdf = await TextoDePdf(pdf);
            if (textoPdf == null) return null;
            var a = Palabras(textoPdf).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            var b = Palabras(string.Join("\n", DocxService.ExtraerParrafos(docx).Select(p => p.Texto))).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            var total = a.Values.Sum();
            if (total == 0) return null;
            var comunes = a.Sum(kv => Math.Min(kv.Value, b.GetValueOrDefault(kv.Key)));
            var cobertura = (double)comunes / total;
            if (cobertura >= 0.985) return null;
            return $"Al convertir el PDF, aproximadamente el {Math.Round((1 - cobertura) * 100)}% del texto no coincide con el original. Revisa el documento con cuidado o sube la versión en Word.";
        }
        catch (Exception ex) { Console.Error.WriteLine($"Verificación del PDF: {ex.Message}"); return null; }
    }

    // El texto del PDF se obtiene con LibreOffice (sin depender de otras herramientas).
    static async Task<string?> TextoDePdf(byte[] pdf)
    {
        var soffice = LibreOfficeService.Ruta;
        if (soffice == null) return null;
        var dir = Path.Combine(Path.GetTempPath(), "vr-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(dir, "in.pdf"), pdf);
            var psi = new ProcessStartInfo(soffice) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in new[] { $"-env:UserInstallation=file://{dir}/perfil", "--headless", "--norestore", "--infilter=writer_pdf_import", "--convert-to", "txt:Text", "--outdir", dir, Path.Combine(dir, "in.pdf") })
                psi.ArgumentList.Add(a);
            psi.Environment["HOME"] = dir;
            using var p = Process.Start(psi)!;
            _ = p.StandardOutput.ReadToEndAsync(); _ = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await p.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { try { p.Kill(true); } catch { } return null; }
            var txt = Path.Combine(dir, "in.txt");
            return File.Exists(txt) ? await File.ReadAllTextAsync(txt) : null;
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── Bloques → Word ────────────────────────────────────────────────────────────────────────
    const string Fuente = "Arial";

    static Paragraph Parrafo(JsonNode? n)
    {
        var tam = Js.Str(n?["tamano"]) switch { "grande" => 28, "pequeno" => 18, _ => 22 };
        var jc = Js.Str(n?["alineacion"]) switch
        {
            "centro" => JustificationValues.Center, "derecha" => JustificationValues.Right,
            "justificado" => JustificationValues.Both, _ => JustificationValues.Left,
        };
        var antes = Js.Str(n?["espacio_antes"]) == "grande" ? 600 : 0;
        var pPr = new ParagraphProperties();
        if (n?["linea_arriba"]?.GetValue<bool>() == true)
            pPr.Append(new ParagraphBorders(new TopBorder { Val = BorderValues.Single, Size = 6, Space = 4, Color = "000000" }));
        pPr.Append(new SpacingBetweenLines { Before = antes.ToString(), After = "120", Line = "276", LineRule = LineSpacingRuleValues.Auto });
        pPr.Append(new Justification { Val = jc });
        var p = new Paragraph(pPr);
        foreach (var r in n?["runs"] as JsonArray ?? new JsonArray())
        {
            var texto = Js.Str(r?["texto"]); if (string.IsNullOrEmpty(texto)) continue;
            var rp = new RunProperties(new RunFonts { Ascii = Fuente, HighAnsi = Fuente, ComplexScript = Fuente });
            if (r?["negrita"]?.GetValue<bool>() == true) rp.Append(new Bold());
            if (r?["cursiva"]?.GetValue<bool>() == true) rp.Append(new Italic());
            rp.Append(new FontSize { Val = tam.ToString() });
            p.Append(new Run(rp, new Text(texto) { Space = SpaceProcessingModeValues.Preserve }));
        }
        return p;
    }

    static byte[] Construir(JsonArray bloques)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            main.Document = new Document(body);
            foreach (var b in bloques)
            {
                switch (Js.Str(b?["tipo"]))
                {
                    case "parrafo": body.Append(Parrafo(b)); break;
                    case "salto_pagina": body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page }))); break;
                    case "tabla":
                    {
                        var filas = b?["filas"] as JsonArray; if (filas == null || filas.Count == 0) break;
                        var cols = Math.Max(1, filas.Max(f => (f as JsonArray)?.Count ?? 1));
                        var ancho = 9600 / cols;
                        var props = new TableProperties(new TableWidth { Width = "9600", Type = TableWidthUnitValues.Dxa });
                        if (b?["bordes"]?.GetValue<bool>() == true)
                            props.Append(new TableBorders(
                                new TopBorder { Val = BorderValues.Single, Size = 4 }, new LeftBorder { Val = BorderValues.Single, Size = 4 },
                                new BottomBorder { Val = BorderValues.Single, Size = 4 }, new RightBorder { Val = BorderValues.Single, Size = 4 },
                                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 }, new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }));
                        var grid = new TableGrid(); for (var i = 0; i < cols; i++) grid.Append(new GridColumn { Width = ancho.ToString() });
                        var t = new Table(props, grid);
                        foreach (var fila in filas.OfType<JsonArray>())
                        {
                            var tr = new TableRow();
                            for (var c = 0; c < cols; c++)
                            {
                                var cell = new TableCell(new TableCellProperties(new TableCellWidth { Width = ancho.ToString(), Type = TableWidthUnitValues.Dxa }));
                                var ps = c < fila.Count ? fila[c] as JsonArray : null;
                                if (ps != null) foreach (var p in ps) cell.Append(Parrafo(p));
                                if (!cell.Elements<Paragraph>().Any()) cell.Append(new Paragraph());
                                tr.Append(cell);
                            }
                            t.Append(tr);
                        }
                        body.Append(t);
                        body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "60" })));
                        break;
                    }
                }
            }
            body.Append(new SectionProperties(new PageSize { Width = 12240, Height = 15840 },
                new PageMargin { Top = 1417, Bottom = 1417, Left = 1320, Right = 1320, Header = 567, Footer = 567 }));
            main.Document.Save();
        }
        return ms.ToArray();
    }
}
