using System.Globalization;
using System.Text;

namespace VertexRent.Api;

/// <summary>Respuestas de archivos con nombre seguro en Content-Disposition.</summary>
public static class Descargas
{
    public static IResult Pdf(HttpContext c, byte[] pdf, string filename)
    {
        var ascii = new string(filename.Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Select(ch => ch < 128 && ch != '"' && ch != '\\' ? ch : '_').ToArray());
        c.Response.Headers.ContentDisposition = $"inline; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(filename)}";
        return Results.File(pdf, "application/pdf");
    }

    public static IResult Docx(HttpContext c, byte[] bytes, string filename)
    {
        var ascii = System.Text.RegularExpressions.Regex.Replace(filename.Normalize(NormalizationForm.FormD), @"\p{M}", "");
        ascii = System.Text.RegularExpressions.Regex.Replace(ascii, @"[^\x20-\x7E]|[""\\]", "_");
        c.Response.Headers.ContentDisposition = $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(filename)}";
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }
}
