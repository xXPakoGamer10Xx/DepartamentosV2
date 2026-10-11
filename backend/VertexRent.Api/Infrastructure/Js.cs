using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Helpers que replican semántica de JS usada por los controladores originales.</summary>
public static class Js
{
    public static bool T(JsonNode? n)
    {
        if (n == null) return false;
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s.Length > 0;
            if (v.TryGetValue<bool>(out var b)) return b;
            if (v.TryGetValue<double>(out var d)) return d != 0 && !double.IsNaN(d);
            if (v.TryGetValue<decimal>(out var m)) return m != 0;
            return true;
        }
        return true;
    }

    /// <summary>Equivalente a a || b || c: el primer valor "truthy" (o el último).</summary>
    public static JsonNode? Or(params JsonNode?[] xs)
    {
        foreach (var x in xs) if (T(x)) return x;
        return xs.Length > 0 ? xs[^1] : null;
    }

    public static string? Str(JsonNode? n)
    {
        if (n == null) return null;
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            return v.ToJsonString();
        }
        return n.ToJsonString();
    }

    public static string TitleCase(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var parts = text.ToLowerInvariant().Split(' ');
        for (var i = 0; i < parts.Length; i++)
            if (parts[i].Length > 0) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i][1..];
        return string.Join(' ', parts);
    }

    /// <summary>toLocaleString('es-MX', {style:'currency', currency:'MXN'}) → "$1,500.00"</summary>
    public static string MXN(decimal amount) => "$" + amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    /// <summary>n.toLocaleString('es-MX') → "1,500.5"</summary>
    public static string NumMx(double n) => n.ToString("#,##0.###", CultureInfo.InvariantCulture);

    /// <summary>parseFloat(): número inicial de un string/número JSON; NaN si no hay.</summary>
    public static double ParseFloat(JsonNode? n)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<decimal>(out var m)) return (double)m;
            if (v.TryGetValue<string>(out var s)) return ParseFloat(s);
        }
        return double.NaN;
    }

    public static double ParseFloat(string? s)
    {
        if (s == null) return double.NaN;
        var m = System.Text.RegularExpressions.Regex.Match(s.TrimStart(), @"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?");
        return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
    }

    /// <summary>Periodo actual "YYYY-MM" en la zona horaria del servidor (igual que new Date() en Node).</summary>
    public static string CurrentPeriodo() { var n = DateTime.Now; return $"{n.Year}-{n.Month:00}"; }

    /// <summary>Lanza una tarea en segundo plano ignorando errores (equivalente a .catch(() => {})).</summary>
    public static void Bg(Func<Task> work) => _ = Task.Run(async () => { try { await work(); } catch { } });

    public static readonly TimeZoneInfo Cdmx = ResolveCdmx();
    static TimeZoneInfo ResolveCdmx()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City"); }
        catch { return TimeZoneInfo.Local; }
    }
    public static DateTime NowCdmx() => TimeZoneInfo.ConvertTime(DateTime.UtcNow, Cdmx);
}

public static class JsonX
{
    public static JsonArray Arr(IEnumerable<string> items) => new(items.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());
    public static string? S(this JsonObject o, string k) => Js.Str(o[k]);
    public static bool Has(this JsonObject o, string k) => o.ContainsKey(k);
    public static JsonNode? N(this JsonObject o, string k) => o.TryGetPropertyValue(k, out var v) ? v : null;
}
