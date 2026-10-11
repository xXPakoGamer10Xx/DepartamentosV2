using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VertexRent.Api;

public sealed class Sugerencia
{
    public string P { get; set; } = "";
    public int Ini { get; set; }
    public int Fin { get; set; }
    public string Texto { get; set; } = "";
    public string Key { get; set; } = "";
    public double Confianza { get; set; }
    public JsonObject? Campo { get; set; }   // definición si la variable es un campo personalizado nuevo
    public string[]? Borrar { get; set; }    // párrafos que se eliminan al aplicar (resto de una lista)

    public JsonObject ToJson() => new()
    {
        ["p"] = P, ["ini"] = Ini, ["fin"] = Fin, ["texto"] = Texto, ["key"] = Key, ["confianza"] = Confianza,
        ["campo"] = Campo?.DeepClone(),
        ["borrar"] = Borrar == null ? null : new JsonArray(Borrar.Select(b => (JsonNode)b!).ToArray()),
    };
}

/// <summary>
/// Pide a Gemini que LOCALICE los datos concretos de un contrato (nombres, montos, fechas, blancos) y los asocie a
/// variables. La IA nunca reescribe el documento: el servidor valida cada sugerencia contra el texto real.
/// </summary>
public static class ContratoAnalisisService
{
    static readonly Regex ClaveCampo = new(@"^[a-z][a-z0-9_]{1,40}$", RegexOptions.Compiled);

    public static bool Disponible => !string.IsNullOrEmpty(Env.Get("GOOGLE_AI_API_KEY"));

    public static async Task<List<Sugerencia>> Sugerir(byte[] docx)
    {
        var parrafos = DocxService.ExtraerParrafos(docx);
        if (parrafos.Count == 0) return new();

        var prompt = new StringBuilder();
        prompt.AppendLine("Eres un asistente que prepara plantillas de contratos de arrendamiento en español (México).");
        prompt.AppendLine("Recibirás los párrafos de un contrato real, ya llenado o con espacios en blanco. Tu trabajo es LOCALIZAR los datos");
        prompt.AppendLine("concretos de ese contrato en particular y decir a qué variable corresponde cada uno. NO reescribas el contrato.");
        prompt.AppendLine();
        prompt.AppendLine("Variables disponibles (clave: significado, ejemplo):");
        foreach (var v in ContratoVariables.Todas.Where(v => v.Grupo != ContratoVariables.GOtros || v.Key == "observaciones"))
            prompt.AppendLine($"- {v.Key}: {v.Etiqueta} (ej. {v.Ejemplo})");
        prompt.AppendLine();
        prompt.AppendLine("Reglas:");
        prompt.AppendLine("1. Marca SOLO datos propios de este contrato: nombres de personas, direcciones, teléfonos, montos, fechas, número de departamento,");
        prompt.AppendLine("   cuentas bancarias, y también espacios en blanco (____, ......) o marcadores ([NOMBRE], XXXX, <<fecha>>) que esperan un dato.");
        prompt.AppendLine("2. NO marques texto legal, cláusulas, ni artículos de ley, ni números de cláusula.");
        prompt.AppendLine("3. \"texto\" debe copiarse EXACTAMENTE, carácter por carácter, tal como aparece en el párrafo indicado.");
        prompt.AppendLine("4. Montos: marca solo la cifra, sin el símbolo $ (ej. 8,500.00). Monto con letra: solo las palabras, sin paréntesis.");
        prompt.AppendLine("5. Fechas: marca la fecha completa (ej. 1 de marzo de 2025).");
        prompt.AppendLine("6. Si un dato no corresponde a ninguna variable pero claramente cambia de contrato a contrato (ej. uso del inmueble, número de");
        prompt.AppendLine("   cajones de estacionamiento), devuélvelo con \"variable\": null y un \"campo\": {\"key\": \"nombre_en_minusculas_con_guiones_bajos\", \"etiqueta\": \"Pregunta corta\"}.");
        prompt.AppendLine("7. Si el mismo dato aparece varias veces, devuelve una entrada por aparición (distinto párrafo o repite el texto en el mismo).");
        prompt.AppendLine("8. \"confianza\" es un número entre 0 y 1.");
        prompt.AppendLine();
        prompt.AppendLine("Responde ÚNICAMENTE con un arreglo JSON, sin texto adicional ni bloques de código:");
        prompt.AppendLine("[{\"p\":\"b:3\",\"texto\":\"Juan Pérez\",\"variable\":\"nombre_completo\",\"campo\":null,\"confianza\":0.95}]");
        prompt.AppendLine();
        prompt.AppendLine("PÁRRAFOS (formato: id | texto):");
        var total = 0;
        foreach (var p in parrafos.Take(500))
        {
            var t = p.Texto.Length > 1800 ? p.Texto[..1800] : p.Texto;
            total += t.Length;
            if (total > 70_000) break;
            prompt.AppendLine($"{p.Id} | {t}");
        }

        var raw = (await GeminiService.Generate(GeminiService.RequireKey("La IA no está configurada en el servidor"), prompt.ToString(), respuestaJson: true)).Trim();
        var ini = raw.IndexOf('['); var fin = raw.LastIndexOf(']');
        JsonArray? arr = null;
        try { if (ini >= 0 && fin > ini) arr = JsonNode.Parse(raw[ini..(fin + 1)]) as JsonArray; } catch { }
        if (arr == null)
        {
            Console.Error.WriteLine($"Análisis IA sin resultados utilizables: {raw[..Math.Min(300, raw.Length)]}");
            throw new AppError("La IA no devolvió una respuesta válida", 502);
        }

        var porId = parrafos.ToDictionary(p => p.Id, p => p.Texto);
        var res = new List<Sugerencia>();
        foreach (var it in arr)
        {
            var pid = Js.Str(it?["p"]); var texto = Js.Str(it?["texto"]);
            if (pid == null || string.IsNullOrEmpty(texto) || !porId.TryGetValue(pid, out var parr)) continue;

            var key = Js.Str(it?["variable"]); JsonObject? campo = null;
            if (string.IsNullOrEmpty(key) || !ContratoVariables.Claves.Contains(key))
            {
                var ck = Js.Str(it?["campo"]?["key"]);
                if (string.IsNullOrEmpty(ck)) continue;
                ck = Regex.Replace(ck.ToLowerInvariant(), @"[^a-z0-9_]", "_");
                ck = ck.StartsWith("campo_") ? ck : "campo_" + ck;
                if (!ClaveCampo.IsMatch(ck)) continue;
                key = ck;
                campo = new JsonObject
                {
                    ["key"] = ck,
                    ["etiqueta"] = (Js.Str(it?["campo"]?["etiqueta"]) ?? ck).Trim(),
                    ["tipo"] = "texto",
                    ["requerido"] = false,
                };
            }

            // Busca la primera aparición del texto que no choque con otra sugerencia ni con una {{variable}} existente.
            var desde = 0; int pos;
            do
            {
                pos = parr.IndexOf(texto, desde, StringComparison.Ordinal);
                if (pos < 0) break;
                var choca = res.Any(s => s.P == pid && pos < s.Fin && pos + texto.Length > s.Ini)
                    || Regex.Matches(parr, @"\{\{[^}]*\}\}").Any(m => pos < m.Index + m.Length && pos + texto.Length > m.Index);
                if (!choca) break;
                desde = pos + 1;
            } while (desde < parr.Length);
            if (pos < 0) continue;

            res.Add(new Sugerencia
            {
                P = pid, Ini = pos, Fin = pos + texto.Length, Texto = texto, Key = key!, Campo = campo,
                Confianza = double.TryParse(Js.Str(it?["confianza"]), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var c) ? Math.Clamp(c, 0, 1) : 0.5,
            });
        }
        FusionarInventario(res, porId);
        return res.OrderBy(s => s.P, StringComparer.Ordinal).ThenBy(s => s.Ini).ToList();
    }

    // Una lista de inventario con un objeto por párrafo ("• Llaves", "• Frigobar"…) se reemplaza por UNA variable
    // que imprime todo el inventario; los demás párrafos de la lista se eliminan.
    static void FusionarInventario(List<Sugerencia> res, Dictionary<string, string> porId)
    {
        static (string Parte, int N)? Id(string p) { var x = p.Split(':'); return x.Length == 2 && int.TryParse(x[1], out var n) ? (x[0], n) : null; }
        var inv = res.Where(s => s.Key == "inventario" && Id(s.P) != null).OrderBy(s => Id(s.P)!.Value.Parte, StringComparer.Ordinal).ThenBy(s => Id(s.P)!.Value.N).ToList();
        var i = 0;
        while (i < inv.Count)
        {
            var j = i;
            while (j + 1 < inv.Count && Id(inv[j + 1].P)!.Value.Parte == Id(inv[i].P)!.Value.Parte && Id(inv[j + 1].P)!.Value.N == Id(inv[j].P)!.Value.N + 1) j++;
            if (j > i)
            {
                var primero = inv[i];
                primero.Key = "inventario"; primero.Ini = 0; primero.Fin = porId[primero.P].Length; primero.Texto = porId[primero.P];
                primero.Borrar = inv.Skip(i + 1).Take(j - i).Select(s => s.P).ToArray();
                foreach (var otro in inv.Skip(i + 1).Take(j - i)) res.Remove(otro);
            }
            i = j + 1;
        }
    }
}
