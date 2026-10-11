using System.Text;
using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Cliente REST mínimo para Google Gemini (generateContent).</summary>
public static class GeminiService
{
    const string Model = "gemini-3.5-flash";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static string RequireKey(string notConfiguredMessage)
    {
        var key = Env.Get("GOOGLE_AI_API_KEY");
        if (string.IsNullOrEmpty(key)) throw new AppError(notConfiguredMessage, 500);
        return key;
    }

    /// <param name="inlineImage">(mimeType, base64) opcional; se envía antes del texto.</param>
    public static async Task<string> Generate(string apiKey, string prompt, (string mime, string data)? inlineImage = null, bool respuestaJson = false, bool pensamientoMinimo = false)
    {
        var parts = new JsonArray();
        if (inlineImage is { } img)
            parts.Add(new JsonObject { ["inline_data"] = new JsonObject { ["mime_type"] = img.mime, ["data"] = img.data } });
        parts.Add(new JsonObject { ["text"] = prompt });
        var body = new JsonObject { ["contents"] = new JsonArray(new JsonObject { ["parts"] = parts }) };
        if (respuestaJson || pensamientoMinimo)
        {
            var cfg = new JsonObject { ["temperature"] = 0 };
            if (respuestaJson) cfg["responseMimeType"] = "application/json";
            // Transcribir no requiere razonar: con el pensamiento al mínimo tarda segundos en vez de minutos.
            if (pensamientoMinimo) cfg["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = "minimal" };
            body["generationConfig"] = cfg;
        }

        // Gemini responde 503 en picos de demanda: se reintenta unas veces antes de rendirse (un 429 por cuota no se reintenta).
        string text; HttpResponseMessage res; JsonNode? json = null;
        for (var intento = 0; ; intento++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("x-goog-api-key", apiKey);
            res = await Http.SendAsync(req);
            text = await res.Content.ReadAsStringAsync();
            if (res.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable || intento >= 3) break;
            await Task.Delay(TimeSpan.FromSeconds(2 * (intento + 1) * (intento + 1)));
        }
        try { json = JsonNode.Parse(text); } catch { }

        if (!res.IsSuccessStatusCode)
        {
            var msg = Js.Str(json?["error"]?["message"]) ?? text;
            throw new AppError($"[GoogleGenerativeAI Error]: {msg}", (int)res.StatusCode);
        }

        var sb = new StringBuilder();
        if (json?["candidates"]?[0]?["content"]?["parts"] is JsonArray ps)
            foreach (var p in ps) sb.Append(Js.Str(p?["text"]));
        return sb.ToString();
    }
}
