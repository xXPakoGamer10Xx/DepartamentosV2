using Microsoft.AspNetCore.HttpOverrides;
using VertexRent.Api;

Env.LoadAll();

var IS_PROD = Env.IsProd;

// ── Modo reinicio: `dotnet VertexRent.Api.dll --reset` ───────────────────────
// Las migraciones normales se aplican solas al arrancar; este modo BORRA toda la base y la recrea.
if (args.Contains("--reset"))
{
    // DESTRUCTIVO: exige confirmar escribiendo el nombre de la base en CONFIRM_RESET.
    var dbName = Env.Get("DB_NAME", "departamentos");
    if (Env.Get("CONFIRM_RESET") != dbName)
    {
        Console.Error.WriteLine($"❌ --reset borra TODOS los datos de '{dbName}'. Para continuar define CONFIRM_RESET={dbName}.");
        Environment.Exit(1);
    }
    Db.Init();
    await Migrations.Reset();
    return;
}

// Las imágenes de .NET definen ASPNETCORE_HTTP_PORTS=8080; el puerto lo manda PORT (UseUrls), así que se descarta.
Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", null);

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10mb, igual que express.json
});
var port = Env.Get("PORT", "3001");
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = null;
    o.SerializerOptions.DictionaryKeyPolicy = null;
    o.SerializerOptions.Encoder = Api.JsonOpts.Encoder;
});

// CORS: en producción solo orígenes permitidos (FRONTEND_URL, separados por coma).
const string ProdDefaultOrigin = "https://laspuercotas.xyz";
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (IS_PROD)
    {
        var fe = Env.Get("FRONTEND_URL");
        var origins = !string.IsNullOrEmpty(fe)
            ? fe.Split(',').Select(s => s.Trim()).ToArray()
            : new[] { ProdDefaultOrigin };
        p.WithOrigins(origins);
    }
    else p.AllowAnyOrigin();
    p.WithMethods("GET", "POST", "PUT", "PATCH", "DELETE").WithHeaders("Content-Type", "Authorization");
}));

builder.Services.AddHostedService<SchedulerService>();
builder.Services.AddHostedService<BackupCronService>();

var app = builder.Build();

// Guard de seguridad: en producción JWT_SECRET debe ser fuerte y no el valor de ejemplo.
var secret = Env.Get("JWT_SECRET");
var inseguros = new[] { "cambia_este_secreto_en_produccion", "cambia_este_secreto_muy_largo_y_seguro_2024" };
if (IS_PROD && (string.IsNullOrEmpty(secret) || secret.Length < 32 || inseguros.Contains(secret)))
{
    Console.Error.WriteLine("❌ JWT_SECRET inseguro o ausente en producción. Define un JWT_SECRET aleatorio (>=32 chars) en el .env.");
    Environment.Exit(1);
}

// Guard de seguridad: en producción la contraseña de la base de datos no puede faltar ni ser la de ejemplo.
var dbPass = Env.Get("DB_PASSWORD");
if (IS_PROD && (string.IsNullOrEmpty(dbPass) || dbPass == "depas_secret_2024"))
{
    Console.Error.WriteLine("❌ DB_PASSWORD ausente o igual a la de ejemplo en producción. Define una contraseña propia en el .env.");
    Environment.Exit(1);
}

foreach (var e in Env.PlatformEmailsNoCanonicos())
    Console.Error.WriteLine($"⚠️  PLATFORM_ADMIN_EMAILS: '{e}' no está en su forma canónica y no coincidirá con cuentas nuevas (usa minúsculas, sin puntos ni +alias en Gmail).");

try
{
    Db.Init();
    await Migrations.Apply();
    await ContratoMigracion.Run();
    // Mientras el dueño no tenga cuenta, cualquiera podría registrar esa dirección y heredar el acceso a los respaldos.
    foreach (var correo in Env.PlatformEmailsSinCuenta().ToList())
        Console.Error.WriteLine($"⚠️  PLATFORM_ADMIN_EMAILS: '{correo}' aún no tiene cuenta. Regístrala TÚ primero: quien la cree obtiene acceso a los respaldos.");
    Console.WriteLine("✅ Conexión a PostgreSQL establecida");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"❌ Error al iniciar el servidor: {ex}");
    Environment.Exit(1);
}

// Detrás de nginx/reverse-proxy: necesario para leer la IP real (rate-limit).
var fwd = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, ForwardLimit = 1 };
fwd.KnownIPNetworks.Clear();
fwd.KnownProxies.Clear();
app.UseForwardedHeaders(fwd);

// Manejo global de errores: {success:false,message}. Los 500 nunca exponen detalles internos.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"❌ Error en {ctx.Request.Method} {ctx.Request.Path}: {(ex is Npgsql.PostgresException p0 ? $"{p0.SqlState} {p0.MessageText}" : ex.Message)}");
        if (ctx.Response.HasStarted) return;
        var (status, msg) = ex switch
        {
            AppError ae => (ae.StatusCode, ae.Message),
            BadHttpRequestException bh => (bh.StatusCode, "Solicitud inválida"),
            // Identificadores o fechas con formato inválido, valores fuera de rango, datos que violan una restricción
            Npgsql.PostgresException { SqlState: "22P02" or "22007" or "22008" or "22003" or "22001" or "23502" or "23514" } => (400, "Datos inválidos"),
            Npgsql.PostgresException { SqlState: "23503" } => (409, "El registro está relacionado con otros datos"),
            Npgsql.PostgresException { SqlState: "23505" } => (409, "Ya existe un registro con esos datos"),
            _ => (500, "Error interno del servidor"),
        };
        var body = new Dictionary<string, object?> { ["success"] = false, ["message"] = string.IsNullOrEmpty(msg) ? "Error interno del servidor" : msg };
        if (Env.IsDev) body["stack"] = ex.StackTrace;
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(body, Api.JsonOpts));
    }
});

// Cabeceras de seguridad (equivalente a helmet).
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        // CSP en modo solo-reporte: se observa en la consola del navegador antes de hacerla obligatoria.
        h["Content-Security-Policy-Report-Only"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
            "font-src 'self' https://fonts.gstatic.com data:; img-src 'self' data: blob:; connect-src 'self'; " +
            "frame-src 'self' blob:; worker-src 'self'; manifest-src 'self'; object-src 'none'; base-uri 'self'; " +
            "form-action 'self'; frame-ancestors 'self'";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["Cross-Origin-Resource-Policy"] = "cross-origin";
        h["Origin-Agent-Cluster"] = "?1";
        h["Referrer-Policy"] = "no-referrer";
        h["Strict-Transport-Security"] = "max-age=15552000; includeSubDomains";
        h["X-Content-Type-Options"] = "nosniff";
        h["X-DNS-Prefetch-Control"] = "off";
        h["X-Download-Options"] = "noopen";
        h["X-Frame-Options"] = "SAMEORIGIN";
        h["X-Permitted-Cross-Domain-Policies"] = "none";
        h["X-XSS-Protection"] = "0";
        return Task.CompletedTask;
    });
    await next();
});

app.UseCors();

// Logger de peticiones
app.Use(async (ctx, next) =>
{
    Console.WriteLine($"[{DateTime.UtcNow:O}] {ctx.Request.Method} {ctx.Request.Path}");
    await next();
});

// Rate limiting general sobre /api (los limitadores estrictos van por ruta).
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        if (!RateLimits.General.Hit($"general:{Api.Ip(ctx)}"))
            throw new AppError(RateLimits.General.Message, 429);
    }
    await next();
});

// Health check mínimo, sin exponer entorno/versión.
app.MapGet("/health", () => Api.Json(new { status = "ok" }));

// Service Worker (PWA + Web Push): lo genera el build del frontend en ./public. Debe vivir en la raíz para tener scope total.
app.MapGet("/sw.js", async (HttpContext c) =>
{
    c.Response.Headers.CacheControl = "no-cache";
    foreach (var root in new[] { Path.Combine(AppContext.BaseDirectory, "public"), Path.Combine(Directory.GetCurrentDirectory(), "public") })
    {
        var file = Path.Combine(root, "sw.js");
        if (!File.Exists(file)) continue;
        c.Response.ContentType = "application/javascript; charset=utf-8";
        await c.Response.SendFileAsync(file);
        return;
    }
    c.Response.StatusCode = 404;
});

AuthEndpoints.Map(app);
InquilinosEndpoints.Map(app);
DepartamentosEndpoints.Map(app);
UsuariosEndpoints.Map(app);
BackupsEndpoints.Map(app);
ConfigEndpoints.Map(app);
CuentasBancariasEndpoints.Map(app);
PagosEndpoints.Map(app);
TicketsEndpoints.Map(app);
CuotasEndpoints.Map(app);
PushEndpoints.Map(app);
EventsEndpoints.Map(app);
NotificacionesEndpoints.Map(app);
InviteCodesEndpoints.Map(app);
DepositosEndpoints.Map(app);
ReportesEndpoints.Map(app);
ContratosEndpoints.Map(app);

// Frontend estático (SPA) con fallback a index.html
var publicPath = Path.Combine(AppContext.BaseDirectory, "public");
if (!Directory.Exists(publicPath)) publicPath = Path.Combine(Directory.GetCurrentDirectory(), "public");
if (Directory.Exists(publicPath))
{
    var fp = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicPath);
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fp, ServeUnknownFileTypes = true });
    app.MapFallback(async (HttpContext c) =>
    {
        if (c.Request.Method != "GET") { await NotFoundHtml(c); return; }
        c.Response.ContentType = "text/html; charset=utf-8";
        await c.Response.SendFileAsync(fp.GetFileInfo("index.html"));
    });
}
else
{
    // Sin frontend desplegado: cualquier ruta desconocida → 404 con la página de Express.
    app.MapFallback(NotFoundHtml);
}

Console.WriteLine($"🚀 Backend corriendo en http://localhost:{port}");
Console.WriteLine($"📋 Health check: http://localhost:{port}/health");
await app.RunAsync();

// 404 con la misma página que devolvía Express.
static async Task NotFoundHtml(HttpContext ctx)
{
    ctx.Response.StatusCode = 404;
    ctx.Response.ContentType = "text/html; charset=utf-8";
    var path = System.Net.WebUtility.HtmlEncode(ctx.Request.Path.Value ?? "/");
    await ctx.Response.WriteAsync($"<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<title>Error</title>\n</head>\n<body>\n<pre>Cannot {ctx.Request.Method} {path}</pre>\n</body>\n</html>\n");
}

// Permite que las pruebas de integración (WebApplicationFactory) arranquen la API real.
public partial class Program { }
