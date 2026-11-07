using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace VertexRent.Api.Tests;

/// <summary>Arranca la API real contra un PostgreSQL 16 desechable (una vez para todas las pruebas).</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string PlatformEmail = "dueno@plataforma.test";

    readonly PostgreSqlContainer _db = new PostgreSqlBuilder().WithImage("postgres:16-alpine")
        .WithDatabase("departamentos").WithUsername("test").WithPassword("test-password").Build();
    WebApplicationFactory<Program> _factory = null!;
    int _seq;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        // La API lee su configuración del entorno: se define antes de crear el servidor.
        Environment.SetEnvironmentVariable("DB_HOST", _db.Hostname);
        Environment.SetEnvironmentVariable("DB_PORT", _db.GetMappedPublicPort(5432).ToString());
        Environment.SetEnvironmentVariable("DB_NAME", "departamentos");
        Environment.SetEnvironmentVariable("DB_USER", "test");
        Environment.SetEnvironmentVariable("DB_PASSWORD", "test-password");
        Environment.SetEnvironmentVariable("JWT_SECRET", "secreto-de-pruebas-con-mas-de-32-caracteres-aleatorios");
        Environment.SetEnvironmentVariable("PLATFORM_ADMIN_EMAILS", PlatformEmail);
        Environment.SetEnvironmentVariable("BACKUP_CRON", "0 0 1 1 *");
        _factory = new WebApplicationFactory<Program>();
        _ = _factory.Server; // fuerza el arranque (migraciones incluidas)
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _db.DisposeAsync();
    }

    public HttpClient NewClient() => _factory.CreateClient();

    public string UniqueEmail(string prefix) => $"{prefix}{Interlocked.Increment(ref _seq)}@prueba.test";

    /// <summary>Cliente con su sesión ya iniciada.</summary>
    public sealed class Session(HttpClient http, string token, string id, string email)
    {
        public HttpClient Http { get; } = http;
        public string Token { get; private set; } = token;
        public string Id { get; } = id;
        public string Email { get; } = email;
        public void SetToken(string t) { Token = t; Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", t); }

        public async Task<(HttpStatusCode Status, JsonNode? Body)> Send(HttpMethod m, string url, object? body = null)
        {
            using var req = new HttpRequestMessage(m, url);
            if (body != null) req.Content = JsonContent.Create(body);
            using var res = await Http.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            JsonNode? json = null;
            try { json = JsonNode.Parse(text); } catch { }
            return (res.StatusCode, json);
        }
        public Task<(HttpStatusCode Status, JsonNode? Body)> Get(string url) => Send(HttpMethod.Get, url);
        public Task<(HttpStatusCode Status, JsonNode? Body)> Post(string url, object? body = null) => Send(HttpMethod.Post, url, body ?? new { });
        public Task<(HttpStatusCode Status, JsonNode? Body)> Put(string url, object? body = null) => Send(HttpMethod.Put, url, body ?? new { });
        public Task<(HttpStatusCode Status, JsonNode? Body)> Patch(string url, object? body = null) => Send(HttpMethod.Patch, url, body ?? new { });
        public Task<(HttpStatusCode Status, JsonNode? Body)> Delete(string url) => Send(HttpMethod.Delete, url);
    }

    public async Task<Session> Register(string email, string rol, string? inviteCode = null, string password = "Contrasena-123")
    {
        var http = NewClient();
        var res = await http.PostAsJsonAsync("/api/auth/register",
            new { email, password, nombre_completo = "Persona De Prueba", rol, invite_code = inviteCode });
        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
        Assert.True(res.IsSuccessStatusCode, $"registro falló: {json}");
        var s = new Session(http, "", json["data"]!["user"]!["id"]!.GetValue<string>(), email);
        s.SetToken(json["data"]!["token"]!.GetValue<string>());
        return s;
    }

    public async Task<Session> Login(string email, string password = "Contrasena-123")
    {
        var http = NewClient();
        var res = await http.PostAsJsonAsync("/api/auth/login", new { email, password });
        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
        Assert.True(res.IsSuccessStatusCode, $"login falló: {json}");
        var s = new Session(http, "", json["data"]!["user"]!["id"]!.GetValue<string>(), email);
        s.SetToken(json["data"]!["token"]!.GetValue<string>());
        return s;
    }

    public Task<Session> RegisterAdmin(string? email = null) => Register(email ?? UniqueEmail("admin"), "admin");

    /// <summary>Crea un código de invitación del admin y registra con él a un colaborador o inquilino.</summary>
    public async Task<Session> InviteUser(Session admin, string rol, string[]? permisos = null)
    {
        var (st, body) = await admin.Post("/api/invite-codes", rol == "cobrador"
            ? new { rol, permisos = permisos ?? Array.Empty<string>(), rol_label = "Prueba" }
            : new { rol });
        Assert.Equal(HttpStatusCode.Created, st);
        var code = body!["data"]!["codigo"]!.GetValue<string>();
        return await Register(UniqueEmail(rol), "inquilino", code);
    }

    /// <summary>Departamento + inquilino activo del admin. Devuelve el id del inquilino.</summary>
    public async Task<string> CreateInquilino(Session admin, int depto, decimal renta = 5000m)
    {
        var (s1, _) = await admin.Post("/api/departamentos", new { numero = depto });
        Assert.Equal(HttpStatusCode.Created, s1);
        var (s2, body) = await admin.Post("/api/inquilinos", new
        {
            nombre_completo = "Inquilino Prueba", depto_numero = depto, renta,
            fecha_inicio = DateTime.Today.AddMonths(-1).ToString("yyyy-MM-dd"),
            fecha_termino = DateTime.Today.AddMonths(11).ToString("yyyy-MM-dd"),
            fecha_pago = "5",
        });
        Assert.Equal(HttpStatusCode.Created, s2);
        return body!["data"]!["id"]!.GetValue<string>();
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture> { }
