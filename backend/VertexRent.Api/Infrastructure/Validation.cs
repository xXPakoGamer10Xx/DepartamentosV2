using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Reglas mínimas equivalentes a las usadas con express-validator.</summary>
public sealed class Validator
{
    readonly JsonObject _b;
    readonly List<Dictionary<string, object?>> _errs = new();
    static readonly Regex EmailRx = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

    public Validator(JsonObject body) { _b = body; }

    void Err(string f, string msg) => _errs.Add(new() { ["campo"] = f, ["msg"] = msg });

    static string? AsString(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>isEmail().normalizeEmail(): lowercase y reglas de Gmail (sin puntos ni +subdirección).</summary>
    public Validator Email(string f, string msg = "Email inválido")
    {
        var s = AsString(_b[f]) ?? (_b[f] is JsonValue ? Js.Str(_b[f]) : null);
        if (s == null || !EmailRx.IsMatch(s.Trim())) { Err(f, msg); return this; }
        _b[f] = NormalizeEmail(s.Trim());
        return this;
    }

    public static string NormalizeEmail(string email)
    {
        var at = email.LastIndexOf('@');
        if (at < 0) return email.ToLowerInvariant();
        var local = email[..at];
        var domain = email[(at + 1)..].ToLowerInvariant();
        if (domain is "gmail.com" or "googlemail.com")
        {
            local = local.ToLowerInvariant();
            var plus = local.IndexOf('+');
            if (plus >= 0) local = local[..plus];
            local = local.Replace(".", "");
            domain = "gmail.com";
        }
        else local = local.ToLowerInvariant();
        return $"{local}@{domain}";
    }

    public Validator Str(string f, int min = 0, int? max = null, string? msg = null, bool optional = false, bool trim = false)
    {
        if (optional && !_b.ContainsKey(f)) return this;
        var s = AsString(_b[f]);
        if (s == null)
        {
            // express-validator reporta cada regla fallida: isString y, si hay mínimo, isLength.
            Err(f, "Invalid value");
            if (min > 0) Err(f, msg ?? "Invalid value");
            return this;
        }
        if (trim) { s = s.Trim(); _b[f] = s; }
        if (s.Length < min || (max.HasValue && s.Length > max.Value)) Err(f, msg ?? "Invalid value");
        return this;
    }

    public Validator Match(string f, string pattern, string msg)
    {
        var s = AsString(_b[f]);
        if (s == null || !Regex.IsMatch(s, pattern)) Err(f, msg);
        return this;
    }

    public IResult? Result => _errs.Count == 0 ? null : Api.Json(new Dictionary<string, object?>
    {
        ["success"] = false, ["message"] = "Datos inválidos", ["errors"] = _errs,
    }, 400);
}
