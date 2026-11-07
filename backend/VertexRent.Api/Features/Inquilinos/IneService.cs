using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VertexRent.Api;

/// <summary>Lectura de datos de una credencial INE a partir de una foto, con Gemini.</summary>
public static class IneService
{
    const string Prompt =
        "Eres un extractor de datos de documentos de identidad mexicanos (INE/credencial de elector). " +
        "Analiza la imagen y extrae los campos indicados. " +
        "IMPORTANTE: responde SOLO con el objeto JSON, sin texto adicional, sin bloques de código, sin explicaciones.\n" +
        "Formato exacto requerido:\n" +
        "{\"nombre_completo\":\"\",\"domicilio\":\"\",\"colonia\":\"\",\"municipio\":\"\",\"estado\":\"\",\"cp\":\"\"}\n" +
        "Si un campo no es legible déjalo como \"\".";

    public static async Task<JsonNode?> Extraer(string? imagenBase64)
    {
        if (string.IsNullOrEmpty(imagenBase64)) throw new AppError("imagen_base64 es requerida", 400);
        var apiKey = GeminiService.RequireKey("GOOGLE_AI_API_KEY no configurada en el servidor");

        // "data:image/jpeg;base64,<data>"
        var coma = imagenBase64.IndexOf(',');
        var cabecera = coma > -1 ? imagenBase64[..coma] : "";
        var datos = coma > -1 ? imagenBase64[(coma + 1)..] : imagenBase64;
        var m = Regex.Match(cabecera, @":(.*?);");
        var mime = m.Success ? m.Groups[1].Value : "image/jpeg";

        var raw = (await GeminiService.Generate(apiKey, Prompt, (mime, datos))).Trim();
        // La respuesta trae datos personales de una identificación: en logs solo metadatos (el texto, solo en desarrollo).
        Console.WriteLine($"[extraerIne] respuesta del modelo: {raw.Length} caracteres");
        if (Env.IsDev) Console.WriteLine($"[extraerIne] (dev) {raw[..Math.Min(300, raw.Length)]}");
        try
        {
            var ini = raw.IndexOf('{'); var fin = raw.LastIndexOf('}');
            if (ini == -1 || fin == -1) throw new FormatException("sin llaves");
            return JsonNode.Parse(raw[ini..(fin + 1)]);
        }
        catch
        {
            Console.Error.WriteLine($"[extraerIne] la respuesta no era JSON válido ({raw.Length} caracteres)");
            if (Env.IsDev) Console.Error.WriteLine($"[extraerIne] (dev) texto recibido: {raw}");
            throw new AppError("El modelo no devolvió JSON válido. Intenta con una imagen más clara o mejor iluminación.", 422);
        }
    }
}
