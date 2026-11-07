using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HtmlToOpenXml;

namespace VertexRent.Api;

public static partial class DocxService
{
    static readonly Regex TagRx = new(@"\{\{\s*([\w.]+)\s*\}\}", RegexOptions.Compiled);

    // ── Rellenar {{variables}} en un DOCX (equivalente a docxtemplater) ──────
    public static byte[] FillTemplate(byte[] docx, IReadOnlyDictionary<string, string> vars, bool pruneEmpty = false)
    {
        using var ms = new MemoryStream();
        ms.Write(docx, 0, docx.Length);
        ms.Position = 0;
        using (var doc = WordprocessingDocument.Open(ms, true))
        {
            var main = doc.MainDocumentPart!;
            FillPart(main.Document!, vars, pruneEmpty);
            foreach (var h in main.HeaderParts) if (h.Header != null) FillPart(h.Header, vars, pruneEmpty);
            foreach (var f in main.FooterParts) if (f.Footer != null) FillPart(f.Footer, vars, pruneEmpty);
            doc.Save();
        }
        return ms.ToArray();
    }

    static void FillPart(OpenXmlElement root, IReadOnlyDictionary<string, string> vars, bool pruneEmpty = false)
    {
        var vacios = new List<Paragraph>();
        // Una lista de inventario escrita como un párrafo por línea, cada uno con {{inventario}}, imprimiría la lista completa
        // varias veces: solo el primero de cada bloque consecutivo se conserva.
        var previoEraInv = false;
        foreach (var p in root.Descendants<Paragraph>().ToList())
        {
            var soloInv = Regex.IsMatch(string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim(), @"^\{\{\s*inventario(_lista)?\s*\}\}$");
            if (soloInv && previoEraInv) { p.Remove(); continue; }
            previoEraInv = soloInv;

            // Lista de inventario: un valor de varias líneas en un párrafo que solo contiene la variable se reparte en un
            // párrafo por línea (conservando viñetas y formato del párrafo original).
            if (soloInv)
            {
                var tag = TagRx.Match(string.Concat(p.Descendants<Text>().Select(t => t.Text))).Groups[1].Value;
                var lineas = (vars.GetValueOrDefault(tag) ?? "").Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lineas.Length > 1)
                {
                    Paragraph anterior = p;
                    for (var k = 1; k < lineas.Length; k++)
                    {
                        var clon = (Paragraph)p.CloneNode(true);
                        anterior.InsertAfterSelf(clon);
                        FillParagraph(clon, new Dictionary<string, string> { [tag] = lineas[k] });
                        anterior = clon;
                    }
                    FillParagraph(p, new Dictionary<string, string> { [tag] = lineas[0] });
                    continue;
                }
            }

            var matches = FillParagraph(p, vars);
            if (matches == null) continue;

            if (pruneEmpty && matches.All(m => !vars.TryGetValue(m.Groups[1].Value, out var val) || val.Trim().Length == 0)
                && DebeEliminarse(string.Concat(p.Descendants<Text>().Select(t => t.Text))))
                vacios.Add(p);
        }
        foreach (var p in vacios) EliminarParrafo(p);
    }

    /// <summary>Rellena las {{variables}} de un párrafo; devuelve las etiquetas encontradas (null si no había).</summary>
    static List<Match>? FillParagraph(Paragraph p, IReadOnlyDictionary<string, string> vars)
    {
            var texts = p.Descendants<Text>().ToList();
            if (texts.Count == 0) return null;
            var full = string.Concat(texts.Select(t => t.Text));
            if (!full.Contains("{{")) return null;

            var matches = TagRx.Matches(full).Cast<Match>().ToList();
            if (matches.Count == 0) return null;

            // Las etiquetas pueden venir partidas entre varios runs. Cada una se resuelve en el run donde
            // empieza (conservando su formato) y se elimina su resto de los runs siguientes — como docxtemplater.
            var offset = 0;
            foreach (var t in texts)
            {
                var a = offset; var b = offset + t.Text.Length; offset = b;
                var sb = new StringBuilder();
                var pos = a;
                while (pos < b)
                {
                    var m = matches.FirstOrDefault(x => pos >= x.Index && pos < x.Index + x.Length);
                    if (m == null) { sb.Append(full[pos]); pos++; continue; }
                    if (pos == m.Index) sb.Append(vars.TryGetValue(m.Groups[1].Value, out var v) ? v : "");
                    pos = Math.Min(m.Index + m.Length, b);
                }
                var result = sb.ToString();
                if (result == t.Text) continue;
                SetTextWithBreaks(t, result);
            }

        return matches;
    }

    // Un párrafo cuyas variables quedaron todas vacías se elimina si no conserva texto real
    // (o solo una etiqueta corta tipo "Observaciones:"), igual que PruneEmptySections con HTML.
    static bool DebeEliminarse(string restante)
    {
        var t = restante.Trim();
        if (!Regex.IsMatch(t, @"[\p{L}\p{N}]")) return true;
        return t.Length <= 40 && t.EndsWith(':');
    }

    static void EliminarParrafo(Paragraph p)
    {
        // Una celda de tabla necesita al menos un párrafo: se vacía en lugar de borrarlo.
        if (p.Parent is TableCell cell && cell.Elements<Paragraph>().Count() == 1)
        {
            foreach (var r in p.Elements<Run>().ToList()) r.Remove();
            return;
        }
        p.Remove();
    }

    // Asigna el texto a un w:t; los saltos de línea del valor pasan a ser w:br (docxtemplater `linebreaks`).
    static void SetTextWithBreaks(Text target, string value)
    {
        var lines = value.Replace("\r\n", "\n").Split('\n');
        target.Text = lines[0];
        target.Space = SpaceProcessingModeValues.Preserve;
        OpenXmlElement last = target;
        for (var i = 1; i < lines.Length; i++)
        {
            var br = new Break();
            last.InsertAfterSelf(br);
            var txt = new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve };
            br.InsertAfterSelf(txt);
            last = txt;
        }
    }

    // ── DOCX → HTML (equivalente a mammoth.convertToHtml) ────────────────────
    public static string ToHtml(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        var main = doc.MainDocumentPart!;
        var styleNames = main.StyleDefinitionsPart?.Styles?.Elements<Style>()
            .Where(s => s.StyleId?.Value != null)
            .ToDictionary(s => s.StyleId!.Value!, s => s.StyleName?.Val?.Value ?? "", StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>();
        var numbering = main.NumberingDefinitionsPart?.Numbering;

        var sb = new StringBuilder();
        RenderChildren(main.Document!.Body!.ChildElements, sb, styleNames, numbering);
        return sb.ToString();
    }

    static void RenderChildren(IEnumerable<OpenXmlElement> children, StringBuilder sb, Dictionary<string, string> styleNames, Numbering? numbering)
    {
        string? openList = null;
        void CloseList() { if (openList != null) { sb.Append("</").Append(openList).Append('>'); openList = null; } }

        foreach (var el in children)
        {
            switch (el)
            {
                case Paragraph p:
                {
                    var inner = RenderRuns(p);
                    if (string.IsNullOrWhiteSpace(Regex.Replace(inner, "<[^>]+>", ""))) continue; // mammoth ignora párrafos vacíos

                    var numPr = p.ParagraphProperties?.NumberingProperties;
                    if (numPr?.NumberingId?.Val?.Value is int numId && numId != 0)
                    {
                        var tag = IsBullet(numbering, numId, numPr.NumberingLevelReference?.Val?.Value ?? 0) ? "ul" : "ol";
                        if (openList != tag) { CloseList(); sb.Append('<').Append(tag).Append('>'); openList = tag; }
                        sb.Append("<li>").Append(inner).Append("</li>");
                        continue;
                    }
                    CloseList();
                    var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                    var name = styleId != null && styleNames.TryGetValue(styleId, out var n) ? n : styleId ?? "";
                    var h = Regex.Match(name, @"^heading\s*([1-6])$", RegexOptions.IgnoreCase);
                    var level = h.Success ? Math.Min(int.Parse(h.Groups[1].Value), 3)
                        : name.Equals("title", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    var htag = level > 0 ? $"h{level}" : "p";
                    sb.Append('<').Append(htag).Append('>').Append(inner).Append("</").Append(htag).Append('>');
                    break;
                }
                case Table t:
                    CloseList();
                    sb.Append("<table>");
                    foreach (var row in t.Elements<TableRow>())
                    {
                        sb.Append("<tr>");
                        foreach (var cell in row.Elements<TableCell>())
                        {
                            sb.Append("<td>");
                            RenderChildren(cell.ChildElements, sb, styleNames, numbering);
                            sb.Append("</td>");
                        }
                        sb.Append("</tr>");
                    }
                    sb.Append("</table>");
                    break;
                case SdtBlock sdt when sdt.SdtContentBlock != null:
                    CloseList();
                    RenderChildren(sdt.SdtContentBlock.ChildElements, sb, styleNames, numbering);
                    break;
            }
        }
        CloseList();
    }

    static bool IsBullet(Numbering? numbering, int numId, int level)
    {
        var abstractId = numbering?.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId)?.AbstractNumId?.Val?.Value;
        var lvl = numbering?.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId)
            ?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == level);
        var fmt = lvl?.NumberingFormat?.Val?.Value;
        return fmt == null || fmt == NumberFormatValues.Bullet;
    }

    static string RenderRuns(Paragraph p)
    {
        // Agrupa runs consecutivos con el mismo formato para no fragmentar el HTML.
        var sb = new StringBuilder();
        bool? curBold = null, curItalic = null;
        var buf = new StringBuilder();

        void Flush()
        {
            if (buf.Length == 0) { return; }
            var text = buf.ToString();
            if (curBold == true) text = $"<strong>{text}</strong>";
            if (curItalic == true) text = $"<em>{text}</em>";
            sb.Append(text);
            buf.Clear();
        }

        foreach (var run in p.Descendants<Run>())
        {
            var bold = run.RunProperties?.Bold != null && run.RunProperties.Bold.Val?.Value != false;
            var italic = run.RunProperties?.Italic != null && run.RunProperties.Italic.Val?.Value != false;
            var text = new StringBuilder();
            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case Text t: text.Append(Escape(t.Text)); break;
                    case TabChar: text.Append('\t'); break;
                    case Break: text.Append("<br />"); break;
                }
            }
            if (text.Length == 0) continue;
            if (curBold != bold || curItalic != italic) { Flush(); curBold = bold; curItalic = italic; }
            buf.Append(text);
        }
        Flush();
        return sb.ToString();
    }

    static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ── HTML → DOCX (equivalente a html-to-docx) ─────────────────────────────
    public static async Task<byte[]> FromHtml(string html)
    {
        var wrapped = $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>{html}</body></html>";
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());

            var converter = new HtmlConverter(main);
            var elements = await converter.ParseAsync(wrapped);
            var body = main.Document.Body!;
            foreach (var el in elements)
            {
                // Estilo equivalente al CSS: Arial 11pt, justificado, interlineado 1.5
                if (el is Paragraph p)
                {
                    p.ParagraphProperties ??= new ParagraphProperties();
                    p.ParagraphProperties.SpacingBetweenLines ??= new SpacingBetweenLines { After = "160", Line = "360", LineRule = LineSpacingRuleValues.Auto };
                    p.ParagraphProperties.Justification ??= new Justification { Val = JustificationValues.Both };
                    foreach (var r in p.Descendants<Run>())
                    {
                        r.RunProperties ??= new RunProperties();
                        r.RunProperties.RunFonts ??= new RunFonts { Ascii = "Arial", HighAnsi = "Arial" };
                        r.RunProperties.FontSize ??= new FontSize { Val = "22" };
                    }
                }
                body.Append(el);
            }

            // Pie de página con numeración
            var footerPart = main.AddNewPart<FooterPart>();
            footerPart.Footer = new Footer(new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
                new Run(new FieldCode(" PAGE ")),
                new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
                new Run(new Text("1")),
                new Run(new FieldChar { FieldCharType = FieldCharValues.End })));
            footerPart.Footer.Save();
            var sectPr = body.Elements<SectionProperties>().LastOrDefault() ?? body.AppendChild(new SectionProperties());
            sectPr.Append(new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footerPart) });

            main.Document.Save();
        }
        return ms.ToArray();
    }
}
