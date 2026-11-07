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
    /// <summary>
    /// Dueño de la plataforma: el correo guardado coincide EXACTAMENTE (sin distinguir mayúsculas) con uno de
    /// PLATFORM_ADMIN_EMAILS. No se normaliza (puntos/+alias de Gmail): un alias no debe heredar el privilegio.
    /// </summary>
    public static bool IsPlatformAdmin(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var e = email.Trim().ToLowerInvariant();
        return (Get("PLATFORM_ADMIN_EMAILS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(p => string.Equals(p.ToLowerInvariant(), e, StringComparison.Ordinal));
    }

    static IEnumerable<string> PlatformEmails() => (Get("PLATFORM_ADMIN_EMAILS") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => p.ToLowerInvariant());

    /// <summary>
    /// Un correo que se normaliza (alias de Gmail) a uno del dueño de la plataforma sin ser exactamente ese:
    /// no se permite registrarlo, para que nadie lo use para confundirse con el dueño.
    /// </summary>
    public static bool IsPlatformAlias(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var e = email.Trim().ToLowerInvariant();
        var canon = Validator.NormalizeEmail(e);
        return PlatformEmails().Any(p => p != e && Validator.NormalizeEmail(p) == canon);
    }

    /// <summary>El correo es el del dueño de la plataforma o un alias suyo: nadie puede crear cuentas con él por rutas abiertas.</summary>
    public static bool EsCorreoDePlataforma(string? email) => IsPlatformAdmin(email) || IsPlatformAlias(email);

    /// <summary>Entradas de PLATFORM_ADMIN_EMAILS que difieren de su forma canónica (nunca coincidirán con cuentas nuevas).</summary>
    public static IEnumerable<string> PlatformEmailsNoCanonicos() =>
        PlatformEmails().Where(p => Validator.NormalizeEmail(p) != p);

    /// <summary>Entradas de PLATFORM_ADMIN_EMAILS que todavía no corresponden a ninguna cuenta (requiere la base de datos lista).</summary>
    public static IEnumerable<string> PlatformEmailsSinCuenta() =>
        PlatformEmails().Where(p => Db.Query("SELECT 1 FROM usuarios WHERE email = $1", p).GetAwaiter().GetResult().First == null);

    public static string JwtSecret => Get("JWT_SECRET") ?? "";
}
