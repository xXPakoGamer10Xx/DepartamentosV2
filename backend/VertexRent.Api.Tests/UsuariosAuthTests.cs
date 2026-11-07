using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace VertexRent.Api.Tests;

/// <summary>Fija el comportamiento de usuarios, invitaciones y autenticación (red de seguridad para la reestructura).</summary>
[Collection("api")]
public sealed class UsuariosAuthTests(ApiFixture api)
{
    static string Str(JsonNode? n, params string[] path) { foreach (var p in path) n = n?[p]; return n!.GetValue<string>(); }

    static async Task<(HttpStatusCode, JsonNode?)> Registrar(ApiFixture api, object cuerpo)
    {
        var res = await api.NewClient().PostAsJsonAsync("/api/auth/register", cuerpo);
        JsonNode? j = null; try { j = JsonNode.Parse(await res.Content.ReadAsStringAsync()); } catch { }
        return (res.StatusCode, j);
    }

    // ── Auth ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Registro_valida_campos_y_correos()
    {
        var (s1, b1) = await Registrar(api, new { email = "no-es-correo", password = "Contrasena-123", nombre_completo = "Ana", rol = "admin" });
        Assert.Equal(HttpStatusCode.BadRequest, s1); Assert.NotNull(b1!["errors"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await Registrar(api, new { email = api.UniqueEmail("x"), password = "corta", nombre_completo = "Ana", rol = "admin" })).Item1);
        Assert.Equal(HttpStatusCode.BadRequest, (await Registrar(api, new { email = api.UniqueEmail("x"), password = "Contrasena-123", rol = "admin" })).Item1); // sin nombre
        var email = api.UniqueEmail("dup");
        Assert.Equal(HttpStatusCode.Created, (await Registrar(api, new { email, password = "Contrasena-123", nombre_completo = "ana de la o", rol = "admin" })).Item1);
        Assert.Equal(HttpStatusCode.Conflict, (await Registrar(api, new { email = email.ToUpperInvariant(), password = "Contrasena-123", nombre_completo = "Otra", rol = "admin" })).Item1);
    }

    [Fact]
    public async Task Registro_capitaliza_el_nombre_y_el_admin_no_tiene_permisos_listados()
    {
        var (st, b) = await Registrar(api, new { email = api.UniqueEmail("cap"), password = "Contrasena-123", nombre_completo = "maría josé lópez", rol = "admin" });
        Assert.Equal(HttpStatusCode.Created, st);
        Assert.Equal("María José López", Str(b, "data", "user", "nombre_completo"));
        Assert.Equal("admin", Str(b, "data", "user", "rol"));
        Assert.Null(b!["data"]!["user"]!["permisos"]);
    }

    [Fact]
    public async Task Registro_de_inquilino_exige_un_codigo_valido()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Registrar(api, new { email = api.UniqueEmail("i"), password = "Contrasena-123", nombre_completo = "Inq", rol = "inquilino" })).Item1);
        Assert.Equal(HttpStatusCode.Forbidden, (await Registrar(api, new { email = api.UniqueEmail("i"), password = "Contrasena-123", nombre_completo = "Inq", rol = "inquilino", invite_code = "ZZZZ-ZZZZ" })).Item1);
        var a = await api.RegisterAdmin();
        var inq = await api.InviteUser(a, "inquilino");
        var (_, me) = await inq.Get("/api/auth/me");
        Assert.Equal("inquilino", Str(me, "data", "rol"));
    }

    [Fact]
    public async Task Registro_con_el_token_antiguo_de_un_inquilino_lo_vincula()
    {
        var a = await api.RegisterAdmin();
        var id = await api.CreateInquilino(a, 1);
        var (_, det) = await a.Get($"/api/inquilinos/{id}");
        var token = Str(det, "data", "invitation_token");
        var email = api.UniqueEmail("tok");
        var (st, _) = await Registrar(api, new { email, password = "Contrasena-123", nombre_completo = "Inq Token", rol = "inquilino", invite_code = token });
        Assert.Equal(HttpStatusCode.Created, st);
        var sesion = await api.Login(email);
        Assert.Equal(id, Str(await sesion.Get("/api/inquilinos/mi-depto").ContinueWith(t => t.Result.Body), "data", "id"));
        // el token ya no sirve
        Assert.Equal(HttpStatusCode.Forbidden, (await Registrar(api, new { email = api.UniqueEmail("tok2"), password = "Contrasena-123", nombre_completo = "Otro", rol = "inquilino", invite_code = token })).Item1);
    }

    [Fact]
    public async Task Login_rechaza_credenciales_incorrectas_y_usuarios_inactivos()
    {
        var a = await api.RegisterAdmin();
        var colab = await api.InviteUser(a, "cobrador", new[] { "pagos" });
        var http = api.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/auth/login", new { email = a.Email, password = "mala-contraseña" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/auth/login", new { email = "nadie@prueba.test", password = "Contrasena-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/auth/login", new { email = "correo-roto", password = "x" })).StatusCode);
        await a.Put($"/api/usuarios/{colab.Id}/toggle");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/auth/login", new { email = colab.Email, password = "Contrasena-123" })).StatusCode);
        // el login es insensible a mayúsculas
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/auth/login", new { email = a.Email.ToUpperInvariant(), password = "Contrasena-123" })).StatusCode);
    }

    [Fact]
    public async Task Me_devuelve_el_perfil_y_los_permisos_del_colaborador()
    {
        var a = await api.RegisterAdmin();
        var colab = await api.InviteUser(a, "cobrador", new[] { "pagos", "no-existe" });
        var (_, me) = await colab.Get("/api/auth/me");
        Assert.Equal("cobrador", Str(me, "data", "rol"));
        Assert.Equal("pagos", me!["data"]!["permisos"]![0]!.GetValue<string>());
        Assert.Single(me["data"]!["permisos"]!.AsArray()); // el permiso inventado se descarta
        Assert.Equal("Prueba", Str(me, "data", "rol_label"));
    }

    [Fact]
    public async Task Cambio_de_contrasena_valida_la_actual_y_el_largo()
    {
        var a = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/auth/change-password", new { passwordActual = "incorrecta", passwordNueva = "Nueva-contrasena-1" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/auth/change-password", new { passwordActual = "Contrasena-123", passwordNueva = "corta" })).Status);
    }

    // ── Usuarios ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Usuarios_lista_al_propio_admin_y_a_los_suyos()
    {
        var a = await api.RegisterAdmin(); var b = await api.RegisterAdmin();
        var colab = await api.InviteUser(a, "cobrador", new[] { "pagos" });
        var ids = (await a.Get("/api/usuarios")).Body!["data"]!.AsArray().Select(x => Str(x, "id")).ToList();
        Assert.Contains(a.Id, ids); Assert.Contains(colab.Id, ids); Assert.DoesNotContain(b.Id, ids);
        Assert.Equal(HttpStatusCode.Forbidden, (await colab.Get("/api/usuarios")).Status);
    }

    [Fact]
    public async Task Admin_crea_usuarios_validando_datos_y_permisos()
    {
        var a = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/usuarios", new { email = api.UniqueEmail("u"), password = "corta", nombre_completo = "A", rol = "cobrador" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/usuarios", new { email = api.UniqueEmail("u"), password = "Contrasena-123", nombre_completo = "A", rol = "dios" })).Status);
        var email = api.UniqueEmail("nuevo");
        var (st, c) = await a.Post("/api/usuarios", new { email, password = "Contrasena-123", nombre_completo = "pedro cobrador", rol = "cobrador", rol_label = "Mi rol", permisos = new[] { "pagos", "inventado", "pagos" } });
        Assert.Equal(HttpStatusCode.Created, st);
        Assert.Equal("Pedro Cobrador", Str(c, "data", "nombre_completo"));
        Assert.Single(c!["data"]!["permisos"]!.AsArray());
        Assert.Equal(HttpStatusCode.Conflict, (await a.Post("/api/usuarios", new { email, password = "Contrasena-123", nombre_completo = "Dup", rol = "cobrador" })).Status);
        await api.Login(email);
    }

    [Fact]
    public async Task Toggle_y_baja_respetan_el_alcance_del_propietario()
    {
        var a = await api.RegisterAdmin(); var b = await api.RegisterAdmin();
        var colab = await api.InviteUser(a, "cobrador", new[] { "pagos" });
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Put($"/api/usuarios/{a.Id}/toggle")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Put($"/api/usuarios/{colab.Id}/toggle")).Status);
        Assert.False((await a.Put($"/api/usuarios/{colab.Id}/toggle")).Body!["data"]!["activo"]!.GetValue<bool>());
        Assert.True((await a.Put($"/api/usuarios/{colab.Id}/toggle")).Body!["data"]!["activo"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Delete($"/api/usuarios/{a.Id}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Delete($"/api/usuarios/{colab.Id}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await a.Delete($"/api/usuarios/{colab.Id}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Delete($"/api/usuarios/{colab.Id}")).Status); // ya no está en su alcance
    }

    [Fact]
    public async Task Perfil_actualiza_nombre_y_avatar_validado()
    {
        var a = await api.RegisterAdmin();
        var (st, p) = await a.Patch("/api/usuarios/perfil", new { nombre_completo = "nuevo nombre" });
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.Equal("Nuevo Nombre", Str(p, "data", "nombre_completo"));
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Patch("/api/usuarios/perfil", new { avatar_url = "http://malo.test/x.png" })).Status);
        var (_, ok) = await a.Patch("/api/usuarios/perfil", new { avatar_url = "data:image/png;base64,iVBORw0KGgo=" });
        Assert.StartsWith("data:image/png", Str(ok, "data", "avatar_url"));
        Assert.Equal("Nuevo Nombre", Str(ok, "data", "nombre_completo")); // lo no enviado se conserva
    }

    [Fact]
    public async Task Permisos_solo_se_editan_a_colaboradores_propios()
    {
        var a = await api.RegisterAdmin(); var b = await api.RegisterAdmin();
        var colab = await api.InviteUser(a, "cobrador", new[] { "pagos" });
        var inq = await api.InviteUser(a, "inquilino");
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Patch($"/api/usuarios/{a.Id}/permisos", new { permisos = new[] { "pagos" } })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Patch($"/api/usuarios/{colab.Id}/permisos", new { permisos = new[] { "pagos" } })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Patch($"/api/usuarios/{inq.Id}/permisos", new { permisos = new[] { "pagos" } })).Status);
        var (st, r) = await a.Patch($"/api/usuarios/{colab.Id}/permisos", new { permisos = new[] { "tickets", "reportes", "x" }, rol_label = "Nuevo" });
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.Equal(2, r!["data"]!["permisos"]!.AsArray().Count);
        Assert.Equal("Nuevo", Str(r, "data", "rol_label"));
    }

    // ── Invitaciones ─────────────────────────────────────────────────────────
    [Fact]
    public async Task Invitaciones_crean_con_preset_listan_y_se_revocan()
    {
        var a = await api.RegisterAdmin(); var b = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/invite-codes", new { rol = "admin" })).Status);
        var (st, c) = await a.Post("/api/invite-codes", new { rol = "cobrador", preset = "contador", expira_dias = 3 });
        Assert.Equal(HttpStatusCode.Created, st);
        Assert.Matches(@"^[A-Z2-9]{4}-[A-Z2-9]{4}$", Str(c, "data", "codigo"));
        Assert.Equal("Contador", Str(c, "data", "rol_label"));
        Assert.Contains("reportes", c!["data"]!["permisos"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.NotNull(c["data"]!["expira_en"]);

        var (_, d) = await a.Post("/api/invite-codes", new { });
        Assert.Equal("inquilino", Str(d, "data", "rol")); // por defecto

        Assert.Equal(2, (await a.Get("/api/invite-codes")).Body!["data"]!.AsArray().Count);
        Assert.Empty((await b.Get("/api/invite-codes")).Body!["data"]!.AsArray());
        var id = Str(c, "data", "id");
        Assert.Equal(HttpStatusCode.NotFound, (await b.Delete($"/api/invite-codes/{id}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await a.Delete($"/api/invite-codes/{id}")).Status);
        var colab = await api.InviteUser(a, "cobrador", new[] { "pagos" });
        Assert.Equal(HttpStatusCode.Forbidden, (await colab.Get("/api/invite-codes")).Status);
    }

    [Fact]
    public async Task Invitacion_usada_muestra_quien_la_uso_y_no_se_reutiliza()
    {
        var a = await api.RegisterAdmin();
        var (_, c) = await a.Post("/api/invite-codes", new { rol = "inquilino" });
        var codigo = Str(c, "data", "codigo");
        var nuevo = await api.Register(api.UniqueEmail("usa"), "inquilino", codigo);
        var fila = (await a.Get("/api/invite-codes")).Body!["data"]!.AsArray().First(x => Str(x, "codigo") == codigo)!;
        Assert.True(fila["usado"]!.GetValue<bool>());
        Assert.Equal("Persona De Prueba", Str(fila, "usado_por_nombre"));
        Assert.Equal(nuevo.Id, Str(fila, "usado_por"));
        Assert.Equal(HttpStatusCode.Forbidden, (await Registrar(api, new { email = api.UniqueEmail("r"), password = "Contrasena-123", nombre_completo = "Reuso", rol = "inquilino", invite_code = codigo })).Item1);
    }
}
