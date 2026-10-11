using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VertexRent.Api;

/// <summary>Formateo es-MX sin depender de ICU/CultureInfo (idéntico en Mac y Docker).</summary>
public static class ContractFormat
{
    static readonly string[] Meses =
        { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };

    static readonly Regex DateOnlyRx = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    /// <summary>Convierte un ISO/fecha a fecha de calendario local. Las fechas "YYYY-MM-DD" se toman tal cual.</summary>
    public static DateTime? ParseCalendarDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        iso = iso.Trim();
        if (DateOnlyRx.IsMatch(iso)) return DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d))
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(d, DateTimeKind.Utc), TimeZoneInfo.Local);
        return null;
    }

    /// <summary>"05 de enero de 2026" (día de 2 dígitos) o "5 de enero de 2026".</summary>
    public static string LongDate(DateTime d, bool twoDigitDay) =>
        $"{(twoDigitDay ? d.Day.ToString("00") : d.Day.ToString())} de {Meses[d.Month - 1]} de {d.Year}";

    public static string LongDate(string? iso, bool twoDigitDay)
    {
        var d = ParseCalendarDate(iso);
        return d == null ? "" : LongDate(d.Value, twoDigitDay);
    }

    public static string Today(bool twoDigitDay) => LongDate(DateTime.Now, twoDigitDay);

    /// <summary>Intl.NumberFormat('es-MX', {style:'currency', currency:'MXN'}) → "$3,500.00"</summary>
    public static string Currency(decimal n) => (n < 0 ? "-$" : "$") + Math.Abs(n).ToString("#,##0.00", CultureInfo.InvariantCulture);

    /// <summary>Intl.NumberFormat('es-MX', {minimumFractionDigits: 2}) → "3,500.00"</summary>
    public static string Number2(decimal n) => n.ToString("#,##0.00#", CultureInfo.InvariantCulture);

    // ── Cantidad con letra: 8500 → "OCHO MIL QUINIENTOS PESOS 00/100 M.N." ───────────────────
    static readonly string[] Unidades =
        { "", "UN", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE", "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE",
          "DIECISÉIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE", "VEINTE", "VEINTIÚN", "VEINTIDÓS", "VEINTITRÉS", "VEINTICUATRO", "VEINTICINCO",
          "VEINTISÉIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE" };
    static readonly string[] Decenas = { "", "", "", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA" };
    static readonly string[] Centenas =
        { "", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS" };

    static string Menor1000(int n)
    {
        if (n == 0) return "";
        if (n == 100) return "CIEN";
        var sb = new System.Text.StringBuilder();
        if (n >= 100) { sb.Append(Centenas[n / 100]); n %= 100; if (n > 0) sb.Append(' '); }
        if (n == 0) return sb.ToString();
        if (n < 30) sb.Append(Unidades[n]);
        else
        {
            sb.Append(Decenas[n / 10]);
            if (n % 10 > 0) sb.Append(" Y ").Append(Unidades[n % 10]);
        }
        return sb.ToString();
    }

    static string Entero(long n)
    {
        if (n == 0) return "CERO";
        var partes = new List<string>();
        var millones = n / 1_000_000; var miles = n / 1000 % 1000; var resto = n % 1000;
        if (millones > 0) partes.Add(millones == 1 ? "UN MILLÓN" : Entero(millones) + " MILLONES");
        if (miles > 0) partes.Add(miles == 1 ? "MIL" : Menor1000((int)miles) + " MIL");
        if (resto > 0) partes.Add(Menor1000((int)resto));
        var txt = string.Join(" ", partes);
        // "UN" delante de MIL/MILLONES se apocopa; al final de la cifra queda "UN" (UN PESO) por convención de moneda.
        return txt;
    }

    /// <summary>"OCHO MIL QUINIENTOS PESOS 00/100 M.N."</summary>
    public static string MontoALetras(decimal monto)
    {
        monto = Math.Round(Math.Abs(monto), 2, MidpointRounding.AwayFromZero);
        var entero = (long)Math.Truncate(monto);
        var cent = (int)Math.Round((monto - entero) * 100);
        var moneda = entero == 1 ? "PESO" : (entero % 1_000_000 == 0 && entero > 0 ? "DE PESOS" : "PESOS");
        return $"{Entero(entero)} {moneda} {cent:00}/100 M.N.";
    }
}

/// <summary>Acceso tolerante a los datos del contrato (valores string/numéricos/JSON mezclados).</summary>
public sealed class ContractData
{
    readonly IReadOnlyDictionary<string, object?> _d;
    public ContractData(IReadOnlyDictionary<string, object?> d) { _d = d; }

    public object? Raw(string k) => _d.TryGetValue(k, out var v) ? v : null;

    public string? S(string k)
    {
        var v = Raw(k);
        return v switch
        {
            null => null,
            string s => s,
            JsonNode n => Js.Str(n),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString(),
        };
    }

    public string Str(string k, string fallback = "") => S(k) ?? fallback;

    public decimal? Num(string k)
    {
        var s = S(k);
        return s != null && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public List<string> List(string k)
    {
        var v = Raw(k);
        var res = new List<string>();
        if (v is JsonArray arr) foreach (var x in arr) res.Add(Js.Str(x) ?? "");
        else if (v is IEnumerable<string> en) res.AddRange(en);
        return res;
    }
}
