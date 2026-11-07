using System.Net;
using System.Text.Json.Nodes;

namespace VertexRent.Api.Tests;

/// <summary>Fija el comportamiento del módulo de inquilinos (red de seguridad para la reestructura).</summary>
[Collection("api")]
public sealed class InquilinosTests(ApiFixture api)
{
    static string Hoy(int meses) => DateTime.Today.AddMonths(meses).ToString("yyyy-MM-dd");

    static object Cuerpo(int depto, string nombre = "juan pérez lópez", decimal renta = 4000m) => new
    {
        nombre_completo = nombre, depto_numero = depto, renta, fecha_inicio = Hoy(0), fecha_termino = Hoy(12), fecha_pago = "5",
    };

    static async Task<string> EstadoDepto(ApiFixture.Session a, int depto)
    {
        var (_, b) = await a.Get($"/api/departamentos/{depto}");
        return b!["data"]!["estado"]!.GetValue<string>();
    }

    async Task<ApiFixture.Session> AdminConDeptos(params int[] numeros)
    {
        var a = await api.RegisterAdmin();
        foreach (var n in numeros) Assert.Equal(HttpStatusCode.Created, (await a.Post("/api/departamentos", new { numero = n })).Status);
        return a;
    }

    [Fact]
    public async Task Crear_valida_campos_y_departamento()
    {
        var a = await AdminConDeptos(1);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/inquilinos", new { nombre_completo = "Sin Datos" })).Status);
        var (st, b) = await a.Post("/api/inquilinos", Cuerpo(99));
        Assert.Equal(HttpStatusCode.BadRequest, st);
        Assert.Contains("99", b!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Crear_capitaliza_el_nombre_ocupa_el_departamento_y_audita()
    {
        var a = await AdminConDeptos(1);
        Assert.Equal("disponible", await EstadoDepto(a, 1));
        var (st, b) = await a.Post("/api/inquilinos", new
        {
            nombre_completo = "juan pérez lópez", depto_numero = 1, renta = 4000, fecha_inicio = Hoy(0), fecha_termino = Hoy(12),
            fecha_pago = "5", fiador_nombre = "maria del carmen", domicilio = "  Calle 1  ", deposito = 2000,
        });
        Assert.Equal(HttpStatusCode.Created, st);
        var d = b!["data"]!;
        Assert.Equal("Juan Pérez López", d["nombre_completo"]!.GetValue<string>());
        Assert.Equal("Maria Del Carmen", d["fiador_nombre"]!.GetValue<string>());
        Assert.Equal("Calle 1", d["domicilio"]!.GetValue<string>());
        Assert.Equal("ocupado", await EstadoDepto(a, 1));

        var (_, det) = await a.Get($"/api/inquilinos/{d["id"]!.GetValue<string>()}");
        var historial = det!["data"]!["historial"]!.AsArray();
        Assert.Equal("crear", historial[0]!["accion"]!.GetValue<string>());
    }

    [Fact]
    public async Task Crear_acepta_los_nombres_alternativos_de_campos()
    {
        var a = await AdminConDeptos(1);
        var (st, b) = await a.Post("/api/inquilinos", new { nombre = "ana ruiz", depto = 1, renta = 3000, fechaInicio = Hoy(0), fechaTermino = Hoy(6), fechaPago = "10", metodoPago = "transferencia" });
        Assert.Equal(HttpStatusCode.Created, st);
        Assert.Equal("transferencia", b!["data"]!["metodo_pago"]!.GetValue<string>());
        Assert.Equal("10", b["data"]!["fecha_pago"]!.GetValue<string>());
    }

    [Fact]
    public async Task Listado_filtra_por_texto_estado_y_departamento()
    {
        var a = await AdminConDeptos(1, 2);
        await a.Post("/api/inquilinos", Cuerpo(1, "Laura Gómez"));
        var (_, seg) = await a.Post("/api/inquilinos", Cuerpo(2, "Pedro Díaz"));
        await a.Delete($"/api/inquilinos/{seg!["data"]!["id"]!.GetValue<string>()}");

        var (_, todos) = await a.Get("/api/inquilinos");
        Assert.Equal(2, todos!["total"]!.GetValue<int>());
        Assert.Equal(1, (await a.Get("/api/inquilinos?search=laura")).Body!["total"]!.GetValue<int>());
        Assert.Equal(1, (await a.Get("/api/inquilinos?search=2")).Body!["total"]!.GetValue<int>()); // por número de depto
        Assert.Equal(1, (await a.Get("/api/inquilinos?estado=inactivo")).Body!["total"]!.GetValue<int>());
        Assert.Equal(1, (await a.Get("/api/inquilinos?depto=1")).Body!["total"]!.GetValue<int>());
        Assert.Equal("Laura Gómez", (await a.Get("/api/inquilinos?depto=1")).Body!["data"]![0]!["nombre_completo"]!.GetValue<string>());
    }

    [Fact]
    public async Task Actualizar_conserva_lo_no_enviado_y_sincroniza_departamentos()
    {
        var a = await AdminConDeptos(1, 2);
        var (_, c) = await a.Post("/api/inquilinos", Cuerpo(1, "Carlos Ruiz", 4000));
        var id = c!["data"]!["id"]!.GetValue<string>();

        var (st, u) = await a.Put($"/api/inquilinos/{id}", new { renta = 4500 });
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.Equal("Carlos Ruiz", u!["data"]!["nombre_completo"]!.GetValue<string>());
        Assert.Equal("4500.00", u["data"]!["renta"]!.GetValue<string>());

        // mover al depto 2: el 1 queda libre y el 2 ocupado
        await a.Put($"/api/inquilinos/{id}", new { depto_numero = 2 });
        Assert.Equal("disponible", await EstadoDepto(a, 1));
        Assert.Equal("ocupado", await EstadoDepto(a, 2));

        // archivarlo libera el depto
        await a.Put($"/api/inquilinos/{id}", new { estado = "inactivo" });
        Assert.Equal("disponible", await EstadoDepto(a, 2));
        var (_, det) = await a.Get($"/api/inquilinos/{id}");
        Assert.Contains(det!["data"]!["historial"]!.AsArray(), h => h!["accion"]!.GetValue<string>() == "editar");

        // observaciones vacías se guardan vacías; textos opcionales vacíos se borran
        await a.Put($"/api/inquilinos/{id}", new { observaciones = "", domicilio = "Calle 5" });
        var (_, u2) = await a.Put($"/api/inquilinos/{id}", new { domicilio = "" });
        Assert.Null(u2!["data"]!["domicilio"]);
    }

    [Fact]
    public async Task Dos_inquilinos_en_un_depto_no_lo_liberan_al_dar_de_baja_a_uno()
    {
        var a = await AdminConDeptos(1);
        var (_, p) = await a.Post("/api/inquilinos", Cuerpo(1, "Primero Uno"));
        await a.Post("/api/inquilinos", Cuerpo(1, "Segundo Dos"));
        await a.Delete($"/api/inquilinos/{p!["data"]!["id"]!.GetValue<string>()}");
        Assert.Equal("ocupado", await EstadoDepto(a, 1));
    }

    [Fact]
    public async Task Baja_es_logica_libera_el_depto_y_respeta_al_propietario()
    {
        var a = await AdminConDeptos(1);
        var b = await api.RegisterAdmin();
        var (_, c) = await a.Post("/api/inquilinos", Cuerpo(1));
        var id = c!["data"]!["id"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.NotFound, (await b.Delete($"/api/inquilinos/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get($"/api/inquilinos/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Put($"/api/inquilinos/{id}", new { renta = 1 })).Status);

        Assert.Equal(HttpStatusCode.OK, (await a.Delete($"/api/inquilinos/{id}")).Status);
        Assert.Equal("disponible", await EstadoDepto(a, 1));
        var (_, det) = await a.Get($"/api/inquilinos/{id}");
        Assert.Equal("inactivo", det!["data"]!["estado"]!.GetValue<string>()); // sigue existiendo
    }

    [Fact]
    public async Task Colaborador_solo_edita_con_el_permiso_de_edicion()
    {
        var a = await AdminConDeptos(1);
        var lector = await api.InviteUser(a, "cobrador", new[] { "inquilinos" });
        var editor = await api.InviteUser(a, "cobrador", new[] { "inquilinos", "inquilinos.editar" });
        Assert.Equal(HttpStatusCode.OK, (await lector.Get("/api/inquilinos")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await lector.Post("/api/inquilinos", Cuerpo(1))).Status);
        Assert.Equal(HttpStatusCode.Created, (await editor.Post("/api/inquilinos", Cuerpo(1))).Status);
    }

    [Fact]
    public async Task Vincular_y_desvincular_cuenta_y_mi_depto()
    {
        var a = await AdminConDeptos(1);
        var (_, c) = await a.Post("/api/inquilinos", Cuerpo(1, "Vero Lara"));
        var id = c!["data"]!["id"]!.GetValue<string>();
        var cuenta = await api.InviteUser(a, "inquilino");

        Assert.Null((await cuenta.Get("/api/inquilinos/mi-depto")).Body!["data"]);
        var (sv, v) = await a.Put($"/api/inquilinos/{id}/vincular-usuario", new { usuario_id = cuenta.Id });
        Assert.Equal(HttpStatusCode.OK, sv);
        Assert.Equal(cuenta.Id, v!["data"]!["usuario_id"]!.GetValue<string>());
        Assert.Equal(id, (await cuenta.Get("/api/inquilinos/mi-depto")).Body!["data"]!["id"]!.GetValue<string>());

        var (_, listado) = await a.Get("/api/inquilinos");
        Assert.Equal(cuenta.Id, listado!["data"]![0]!["usuario"]!["id"]!.GetValue<string>());

        await a.Put($"/api/inquilinos/{id}/vincular-usuario", new { usuario_id = (string?)null });
        Assert.Null((await cuenta.Get("/api/inquilinos/mi-depto")).Body!["data"]);
    }

    [Fact]
    public async Task Token_de_pdf_es_del_propietario_y_el_pdf_exige_token()
    {
        var a = await AdminConDeptos(1);
        var b = await api.RegisterAdmin();
        var (_, c) = await a.Post("/api/inquilinos", Cuerpo(1));
        var id = c!["data"]!["id"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.NotFound, (await b.Post($"/api/inquilinos/{id}/pdf-token")).Status);
        var (st, t) = await a.Post($"/api/inquilinos/{id}/pdf-token");
        Assert.Equal(HttpStatusCode.OK, st);
        var token = t!["data"]!["token"]!.GetValue<string>();

        var anonimo = api.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/inquilinos/{id}/pdf")).StatusCode);
        var pdf = await anonimo.GetAsync($"/api/inquilinos/{id}/pdf?token={token}");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync()).Take(4).ToArray()));
        // el token de un inquilino no abre el de otro
        var otro = await AdminConDeptos(1);
        var (_, c2) = await otro.Post("/api/inquilinos", Cuerpo(1));
        Assert.Equal(HttpStatusCode.Forbidden, (await anonimo.GetAsync($"/api/inquilinos/{c2!["data"]!["id"]!.GetValue<string>()}/pdf?token={token}")).StatusCode);
    }

    [Fact]
    public async Task Extraer_ine_es_solo_para_admin_y_exige_imagen()
    {
        var a = await AdminConDeptos(1);
        var colab = await api.InviteUser(a, "cobrador", new[] { "inquilinos", "inquilinos.editar" });
        Assert.Equal(HttpStatusCode.Forbidden, (await colab.Post("/api/inquilinos/extraer-ine", new { imagen_base64 = "x" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/inquilinos/extraer-ine", new { })).Status);
    }
}
