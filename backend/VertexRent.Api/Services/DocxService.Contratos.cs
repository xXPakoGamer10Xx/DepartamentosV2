using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace VertexRent.Api;

/// <summary>Marca aceptada: el rango [Ini, Fin) del texto del párrafo P se sustituirá por {{Key}}.</summary>
public sealed record Marca(string P, int Ini, int Fin, string Key, string[]? Borrar = null);
public sealed record ParrafoInfo(string Id, string Texto);

public static partial class DocxService
{
    public const int MaxBytes = 5 * 1024 * 1024;
    const long MaxDescomprimido = 100L * 1024 * 1024;
    const int MaxEntradas = 3000;

    // ── Validación y saneado de un .docx subido por un usuario ───────────────────────────────
    public static void Validar(byte[] docx)
    {
        if (docx.Length == 0) throw new AppError("El archivo está vacío", 400);
        if (docx.Length > MaxBytes) throw new AppError("El archivo excede el límite de 5 MB", 400);
        try
        {
            using var ms = new MemoryStream(docx);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxEntradas) throw new AppError("El documento tiene demasiados elementos", 400);
            long total = 0;
            var tieneDocumento = false;
            foreach (var e in zip.Entries)
            {
                total += e.Length;
                if (total > MaxDescomprimido) throw new AppError("El documento es demasiado grande al descomprimirse", 400);
                var n = e.FullName.Replace('\\', '/').ToLowerInvariant();
                if (n == "word/document.xml") tieneDocumento = true;
                if (n.Contains("vbaproject") || n.EndsWith("vbadata.xml")) throw new AppError("No se admiten documentos con macros. Guárdalo como .docx normal.", 400);
            }
            if (!tieneDocumento) throw new AppError("El archivo no es un documento de Word (.docx) válido", 400);

            var ct = zip.GetEntry("[Content_Types].xml");
            if (ct != null)
            {
                using var r = new StreamReader(ct.Open());
                if (r.ReadToEnd().Contains("macroEnabled", StringComparison.OrdinalIgnoreCase))
                    throw new AppError("No se admiten documentos con macros (.docm). Guárdalo como .docx normal.", 400);
            }
            using var doc = WordprocessingDocument.Open(new MemoryStream(docx), false);
            if (doc.MainDocumentPart?.Document?.Body == null) throw new AppError("El archivo no es un documento de Word (.docx) válido", 400);
        }
        catch (AppError) { throw; }
        catch (Exception) { throw new AppError("El archivo no es un documento de Word (.docx) válido", 400); }
    }

    /// <summary>Quita relaciones externas que no sean enlaces (imágenes remotas, plantillas adjuntas…) antes de convertir.</summary>
    public static byte[] Sanear(byte[] docx)
    {
        using var ms = new MemoryStream();
        ms.Write(docx, 0, docx.Length);
        ms.Position = 0;
        using (var doc = WordprocessingDocument.Open(ms, true))
        {
            var main = doc.MainDocumentPart!;
            main.DocumentSettingsPart?.Settings?.RemoveAllChildren<AttachedTemplate>();
            Limpiar(main);
            foreach (var h in main.HeaderParts) Limpiar(h);
            foreach (var f in main.FooterParts) Limpiar(f);
            if (main.DocumentSettingsPart != null) Limpiar(main.DocumentSettingsPart);
            doc.Save();
        }
        return ms.ToArray();

        static void Limpiar(OpenXmlPartContainer part)
        {
            foreach (var rel in part.ExternalRelationships.ToList())
                if (!rel.RelationshipType.EndsWith("/hyperlink", StringComparison.OrdinalIgnoreCase))
                    part.DeleteExternalRelationship(rel.Id);
        }
    }

    public static byte[] PonerTitulo(byte[] docx, string titulo)
    {
        using var ms = new MemoryStream();
        ms.Write(docx, 0, docx.Length);
        ms.Position = 0;
        using (var doc = WordprocessingDocument.Open(ms, true))
            doc.PackageProperties.Title = titulo;
        return ms.ToArray();
    }

    // ── Párrafos direccionables: "b:12" (cuerpo), "h0:3" (encabezado 0), "f0:1" (pie 0) ─────────
    static IEnumerable<(string Key, OpenXmlElement Root)> Partes(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart!;
        yield return ("b", main.Document!);
        var i = 0;
        foreach (var h in main.HeaderParts) { if (h.Header != null) yield return ($"h{i}", h.Header); i++; }
        i = 0;
        foreach (var f in main.FooterParts) { if (f.Footer != null) yield return ($"f{i}", f.Footer); i++; }
    }

    static string TextoDe(Paragraph p) => string.Concat(p.Descendants<Text>().Select(t => t.Text));

    public static List<ParrafoInfo> ExtraerParrafos(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx), false);
        var res = new List<ParrafoInfo>();
        foreach (var (key, root) in Partes(doc))
        {
            var idx = 0;
            foreach (var p in root.Descendants<Paragraph>())
            {
                var t = TextoDe(p);
                if (t.Trim().Length > 0) res.Add(new ParrafoInfo($"{key}:{idx}", t));
                idx++;
            }
        }
        return res;
    }

    /// <summary>{{variables}} que ya trae el documento (las plantillas con variables propias no necesitan IA).</summary>
    public static List<string> VariablesPresentes(byte[] docx) =>
        ExtraerParrafos(docx).SelectMany(p => TagRx.Matches(p.Texto).Select(m => m.Groups[1].Value)).Distinct().ToList();

    // ── Aplicar marcas: sustituye cada rango por {{key}} conservando el formato del primer run ──
    public static byte[] AplicarMarcas(byte[] docx, IReadOnlyList<Marca> marcas)
    {
        if (marcas.Count == 0) return docx;
        using var ms = new MemoryStream();
        ms.Write(docx, 0, docx.Length);
        ms.Position = 0;
        using (var doc = WordprocessingDocument.Open(ms, true))
        {
            var porParrafo = marcas.GroupBy(m => m.P).ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.Ini).ToList());
            // Párrafos que se eliminan porque otra marca los reemplaza (p. ej. el resto de una lista de inventario).
            var borrar = marcas.SelectMany(m => m.Borrar ?? Array.Empty<string>()).ToHashSet();
            foreach (var (key, root) in Partes(doc))
            {
                var idx = 0;
                foreach (var p in root.Descendants<Paragraph>().ToList())
                {
                    var id = $"{key}:{idx++}";
                    if (borrar.Contains(id)) { p.Remove(); continue; }
                    if (!porParrafo.TryGetValue(id, out var lista)) continue;
                    foreach (var m in lista)
                    {
                        var texts = p.Descendants<Text>().ToList();
                        var largo = texts.Sum(t => t.Text.Length);
                        if (m.Ini < 0 || m.Fin > largo || m.Fin <= m.Ini) continue;
                        ReemplazarRango(texts, m.Ini, m.Fin - m.Ini, "{{" + m.Key + "}}");
                    }
                }
            }
            doc.Save();
        }
        return ms.ToArray();
    }

    static void ReemplazarRango(List<Text> texts, int start, int len, string reemplazo)
    {
        var offset = 0; var colocado = false;
        foreach (var t in texts)
        {
            var a = offset; var b = a + t.Text.Length; offset = b;
            if (b <= start || a >= start + len) continue;
            var s = Math.Max(start, a) - a; var e = Math.Min(start + len, b) - a;
            t.Text = t.Text[..s] + (colocado ? "" : reemplazo) + t.Text[e..];
            t.Space = SpaceProcessingModeValues.Preserve;
            colocado = true;
        }
    }

    // ── HTML de revisión: párrafos con data-p y las marcas resaltadas ────────────────────────
    public static string HtmlDeRevision(byte[] docx, IReadOnlyList<Marca> marcas)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx), false);
        var sb = new StringBuilder();
        var porParrafo = marcas.GroupBy(m => m.P).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var (key, root) in Partes(doc))
        {
            if (key != "b") sb.Append("<p class=\"parte\">").Append(key[0] == 'h' ? "Encabezado" : "Pie de página").Append("</p>");
            var idx = 0;
            foreach (var p in root.Descendants<Paragraph>())
            {
                var id = $"{key}:{idx++}";
                var chars = new List<(char C, bool B, bool I)>();
                foreach (var t in p.Descendants<Text>())
                {
                    var rp = t.Ancestors<Run>().FirstOrDefault()?.RunProperties;
                    var b = rp?.Bold != null && rp.Bold.Val?.Value != false;
                    var i = rp?.Italic != null && rp.Italic.Val?.Value != false;
                    foreach (var ch in t.Text) chars.Add((ch, b, i));
                }
                if (chars.Count == 0 || new string(chars.Select(c => c.C).ToArray()).Trim().Length == 0) continue;

                // -2 = variable ya presente en el documento, -1 = sin marca, >=0 = índice en la lista de marcas
                var texto = new string(chars.Select(c => c.C).ToArray());
                var tipo = new int[chars.Count]; var claves = new string?[chars.Count];
                Array.Fill(tipo, -1);
                foreach (Match m in TagRx.Matches(texto))
                    for (var k = m.Index; k < m.Index + m.Length; k++) { tipo[k] = -2; claves[k] = m.Groups[1].Value; }
                if (porParrafo.TryGetValue(id, out var ms))
                    foreach (var m in ms)
                        for (var k = Math.Max(0, m.Ini); k < Math.Min(chars.Count, m.Fin); k++) { tipo[k] = marcas.ToList().IndexOf(m); claves[k] = m.Key; }

                var pPr = p.ParagraphProperties;
                var estilo = pPr?.ParagraphStyleId?.Val?.Value ?? "";
                var esTitulo = Regex.IsMatch(estilo, "^(heading|ttulo|title)", RegexOptions.IgnoreCase);
                var jc = pPr?.Justification?.Val?.Value;
                var clase = new List<string>();
                if (p.Ancestors<TableCell>().Any()) clase.Add("celda");
                if (jc == JustificationValues.Center) clase.Add("centro");
                else if (jc == JustificationValues.Right) clase.Add("derecha");
                var tag = esTitulo ? "h2" : "p";
                sb.Append('<').Append(tag).Append(" data-p=\"").Append(id).Append('"');
                if (clase.Count > 0) sb.Append(" class=\"").Append(string.Join(' ', clase)).Append('"');
                sb.Append('>');

                var pos = 0;
                while (pos < chars.Count)
                {
                    var t0 = tipo[pos]; var end = pos;
                    while (end < chars.Count && tipo[end] == t0 && (t0 == -1 || claves[end] == claves[pos])) end++;
                    if (t0 != -1)
                        sb.Append(t0 == -2 ? "<mark class=\"auto\"" : "<mark")
                          .Append(" data-var=\"").Append(WebUtility.HtmlEncode(claves[pos])).Append("\" data-ini=\"").Append(pos)
                          .Append("\" data-fin=\"").Append(end).Append('"').Append(t0 >= 0 ? $" data-i=\"{t0}\"" : "").Append('>');
                    // dentro de cada grupo, se respeta negrita/cursiva
                    var q = pos;
                    while (q < end)
                    {
                        var (_, b, i) = chars[q]; var e2 = q;
                        while (e2 < end && chars[e2].B == b && chars[e2].I == i) e2++;
                        var frag = WebUtility.HtmlEncode(new string(chars.Skip(q).Take(e2 - q).Select(c => c.C).ToArray()));
                        if (b) frag = $"<strong>{frag}</strong>";
                        if (i) frag = $"<em>{frag}</em>";
                        sb.Append(frag);
                        q = e2;
                    }
                    if (t0 != -1) sb.Append("</mark>");
                    pos = end;
                }
                sb.Append("</").Append(tag).Append('>');
            }
        }
        return sb.ToString();
    }
}
