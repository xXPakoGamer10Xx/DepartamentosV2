using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>JWT HS256 mínimo (compatible con los tokens emitidos por jsonwebtoken).</summary>
public static class Jwt
{
    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static byte[] UnB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
    static byte[] Sign(string data) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(Env.JwtSecret), Encoding.UTF8.GetBytes(data));

    public static long ParseExpiry(string? exp)
    {
        if (string.IsNullOrWhiteSpace(exp)) return 7 * 86400;
        exp = exp.Trim();
        if (long.TryParse(exp, out var secs)) return secs;
        var unit = exp[^1];
        if (!long.TryParse(exp[..^1], out var n)) return 7 * 86400;
        return unit switch { 's' => n, 'm' => n * 60, 'h' => n * 3600, 'd' => n * 86400, 'w' => n * 604800, _ => 7 * 86400 };
    }

    public static string Create(JsonObject payload, string? expiresIn = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        payload["iat"] = now;
        payload["exp"] = now + ParseExpiry(expiresIn ?? Env.Get("JWT_EXPIRES_IN", "7d"));
        var header = B64(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var body = B64(Encoding.UTF8.GetBytes(payload.ToJsonString()));
        var sig = B64(Sign($"{header}.{body}"));
        return $"{header}.{body}.{sig}";
    }

    /// <summary>Devuelve el payload si la firma y la expiración son válidas; si no, null.</summary>
    public static JsonObject? Verify(string? token)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(Env.JwtSecret)) return null;
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            var expected = Sign($"{parts[0]}.{parts[1]}");
            if (!CryptographicOperations.FixedTimeEquals(expected, UnB64(parts[2]))) return null;
            var payload = JsonNode.Parse(UnB64(parts[1])) as JsonObject;
            if (payload == null) return null;
            if (payload["exp"] is JsonValue ev && ev.TryGetValue<long>(out var exp) && exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return null;
            return payload;
        }
        catch { return null; }
    }
}
