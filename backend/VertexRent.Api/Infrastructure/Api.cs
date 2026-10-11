using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VertexRent.Api;

public static class Api
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IResult Json(object? body, int status = 200) =>
        Results.Text(JsonSerializer.Serialize(body, JsonOpts), "application/json; charset=utf-8", System.Text.Encoding.UTF8, status);

    public static IResult Data(object? data, int status = 200) =>
        Json(new Dictionary<string, object?> { ["success"] = true, ["data"] = data }, status);

    public static IResult Msg(string message, int status = 200) =>
        Json(new Dictionary<string, object?> { ["success"] = true, ["message"] = message }, status);

    public static IResult Ok(int status = 200) =>
        Json(new Dictionary<string, object?> { ["success"] = true }, status);

    public static IResult Fail(string message, int status) =>
        Json(new Dictionary<string, object?> { ["success"] = false, ["message"] = message }, status);

    /// <summary>Objeto arbitrario con success=true más las propiedades dadas.</summary>
    public static IResult Obj(int status, params (string, object?)[] props)
    {
        var d = new Dictionary<string, object?> { ["success"] = true };
        foreach (var (k, v) in props) d[k] = v;
        return Json(d, status);
    }

    public static async Task<JsonObject> Body(HttpContext c)
    {
        if (c.Items.TryGetValue("__body", out var cached)) return (JsonObject)cached!;
        JsonObject obj = new();
        var ct = c.Request.ContentType ?? "";
        if (c.Request.ContentLength is > 0 || c.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            try
            {
                if (ct.Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    var node = await JsonNode.ParseAsync(c.Request.Body);
                    if (node is JsonObject o) obj = o;
                }
                else if (ct.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                {
                    var form = await c.Request.ReadFormAsync();
                    foreach (var kv in form) obj[kv.Key] = kv.Value.ToString();
                }
            }
            catch (JsonException ex) { throw new AppError(ex.Message, 400); }
        }
        c.Items["__body"] = obj;
        return obj;
    }

    public static string? Query(HttpContext c, string key)
    {
        var v = c.Request.Query[key].ToString();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public static string Route(HttpContext c, string key) => c.Request.RouteValues[key]?.ToString() ?? "";

    public static string? Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString();

    public static List<Row> ToList(QueryResult r) => r.Rows;
}
