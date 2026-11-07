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
}
