namespace VertexRent.Api;

public static class Env
{
    /// <summary>Carga un archivo .env (KEY=VALUE) sin pisar variables ya definidas.</summary>
    /// <summary>Carga el .env del directorio actual y, en desarrollo, el de las carpetas padre (backend/.env).</summary>
    public static void LoadAll()
    {
        foreach (var dir in new[] { ".", "..", "../.." })
            LoadDotEnv(Path.Combine(dir, ".env"));
        LoadDotEnv(Path.Combine(AppContext.BaseDirectory, ".env"));
    }

    public static void LoadDotEnv(string path = ".env")
    {
        if (!File.Exists(path)) return;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            if (val.Length >= 2 && ((val[0] == '"' && val[^1] == '"') || (val[0] == '\'' && val[^1] == '\'')))
                val = val[1..^1];
            if (Environment.GetEnvironmentVariable(key) == null)
                Environment.SetEnvironmentVariable(key, val);
        }
    }

    public static string? Get(string key) => Environment.GetEnvironmentVariable(key);
    public static string Get(string key, string fallback)
    {
        var v = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrEmpty(v) ? fallback : v;
    }

    public static bool IsProd => Get("NODE_ENV") == "production" || Get("ASPNETCORE_ENVIRONMENT") == "Production";
    public static bool IsDev => Get("NODE_ENV") == "development";
    public static string JwtSecret => Get("JWT_SECRET") ?? "";
}
