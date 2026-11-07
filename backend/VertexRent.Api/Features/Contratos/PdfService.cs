using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace VertexRent.Api;

public sealed record TextSegment(string Text, bool Bold = false, bool Italic = false);
public sealed record ParsedPara(string Level, List<TextSegment> Segments);

public static class PdfService
{
    // Liberation Sans (SIL OFL) va desplegada con la app: métricas de Arial/Helvetica y salida
    // idéntica en cualquier entorno, sin depender de las fuentes del sistema.
    const string FontFamily = "Liberation Sans";
    static readonly string[] Fonts = { FontFamily };

    static PdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
        if (Directory.Exists(dir))
            foreach (var ttf in Directory.GetFiles(dir, "*.ttf"))
            {
                using var fs = File.OpenRead(ttf);
                QuestPDF.Drawing.FontManager.RegisterFontFromStream(fs);
            }
    }

    // ── Parseo de HTML (equivalente a parseMammothHtml) ──────────────────────
    static string DecodeEntities(string s) => s
        .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
        .Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&nbsp;", " ");

    static string StripTags(string html) => DecodeEntities(Regex.Replace(html, "<[^>]+>", ""));

    static readonly Regex InlineRx = new(
        @"<strong[^>]*>([\s\S]*?)</strong>|<b[^>]*>([\s\S]*?)</b>|<em[^>]*>([\s\S]*?)</em>|<i[^>]*>([\s\S]*?)</i>|([^<]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static List<TextSegment> ParseInline(string content)
    {
        var segs = new List<TextSegment>();
        foreach (Match m in InlineRx.Matches(content))
        {
            if (m.Groups[1].Success || m.Groups[2].Success)
            {
                var t = StripTags(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
                if (t.Length > 0) segs.Add(new TextSegment(t, Bold: true));
            }
            else if (m.Groups[3].Success || m.Groups[4].Success)
            {
                var t = StripTags(m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value);
                if (t.Length > 0) segs.Add(new TextSegment(t, Italic: true));
            }
            else if (m.Groups[5].Success && m.Groups[5].Value.Length > 0)
            {
                var t = DecodeEntities(m.Groups[5].Value);
                if (t.Trim().Length > 0) segs.Add(new TextSegment(t));
            }
        }
        return segs;
    }

    static readonly Regex BlockRx = new(@"<(p|div|h1|h2|h3|li)[^>]*>([\s\S]*?)</\1>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<ParsedPara> ParseHtml(string html)
    {
        var paras = new List<ParsedPara>();
        foreach (Match m in BlockRx.Matches(html))
        {
            var tag = m.Groups[1].Value.ToLowerInvariant();
            var level = tag.StartsWith('h') ? tag : "p";
            var segs = ParseInline(m.Groups[2].Value);
            if (segs.Count > 0) paras.Add(new ParsedPara(level, segs));
        }
        return paras;
    }

    // ── Variables de plantilla ───────────────────────────────────────────────
    public static Dictionary<string, string> BuildVars(ContractData data) => ContratoVariables.Build(data);

    // ── Limpieza de secciones vacías ─────────────────────────────────────────
    static bool TieneTexto(string fragment)
    {
        var t = Regex.Replace(fragment, "<[^>]+>", "");
        t = Regex.Replace(t, "&[a-z]+;", " ", RegexOptions.IgnoreCase);
        return Regex.IsMatch(t, @"[\p{L}\p{N}]");
    }

    public static string PruneEmptySections(string html)
    {
        // 1. Encabezado + bloque siguiente vacío → se elimina el par
        html = Regex.Replace(html, @"<(h[1-3])\b[^>]*>[\s\S]*?</\1>\s*<(p|div|li)\b[^>]*>([\s\S]*?)</\2>",
            m => TieneTexto(m.Groups[3].Value) ? m.Value : "", RegexOptions.IgnoreCase);
        // 2. Párrafos / bloques vacíos sueltos
        html = Regex.Replace(html, @"<(p|div|li)\b[^>]*>([\s\S]*?)</\1>",
            m => TieneTexto(m.Groups[2].Value) ? m.Value : "", RegexOptions.IgnoreCase);
        // 3. Encabezado sin nada debajo (otro encabezado o fin del documento)
        html = Regex.Replace(html, @"<(h[1-3])\b[^>]*>[\s\S]*?</\1>\s*(?=(<h[1-3]\b)|\s*\z)", "", RegexOptions.IgnoreCase);
        return html;
    }

    // El contrato oficial se genera desde el Word con LibreOffice (ContratoRenderer); este PDF es solo el respaldo aproximado.

    internal static string[] FontFamilies => Fonts;
}
