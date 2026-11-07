using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace VertexRent.Api.Tests;

/// <summary>Fija el comportamiento del módulo de contratos (red de seguridad para la reestructura).</summary>
[Collection("api")]
public sealed class ContratosTests(ApiFixture api)
{
    static string Str(JsonNode? n, params string[] path) { foreach (var p in path) n = n?[p]; return n!.GetValue<string>(); }

    /// <summary>Admin con un inquilino que ya tiene todos los datos que pide el contrato base.</summary>
    async Task<(ApiFixture.Session A, string Inq)> Escenario(int depto = 1)
    {
        var a = await api.RegisterAdmin();
        await a.Post("/api/departamentos", new { numero = depto });
        var (_, b) = await a.Post("/api/inquilinos", new
        {
            nombre_completo = "Contrato Prueba", depto_numero = depto, renta = 4500, deposito = 4500, fecha_pago = "5",
            fecha_inicio = DateTime.Today.ToString("yyyy-MM-dd"), fecha_termino = DateTime.Today.AddMonths(12).ToString("yyyy-MM-dd"),
            domicilio = "Calle 1", identificacion = "INE 123", email_contacto = "x@y.test", tel_arrendatario = "5512345678",
        });
        return (a, Str(b, "data", "id"));
    }

    static async Task<byte[]> DocxSistema(ApiFixture.Session a)
    {
        var res = await a.Http.GetAsync("/api/contratos/plantillas/sistema/docx");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.Content.ReadAsByteArrayAsync();
    }

    /// <summary>Sube el contrato base como plantilla propia y la deja lista.</summary>
    async Task<string> PlantillaPropia(ApiFixture.Session a, string nombre = "Mi contrato")
    {
        var docx = await DocxSistema(a);
        var (st, b) = await a.Post("/api/contratos/plantillas", new { nombre, archivoBase64 = "data:application/vnd.openxmlformats-officedocument.wordprocessingml.document;base64," + Convert.ToBase64String(docx) });
        Assert.Equal(HttpStatusCode.Created, st);
        var id = Str(b, "data", "id");
        Assert.Equal(HttpStatusCode.OK, (await a.Put($"/api/contratos/plantillas/{id}", new { estado = "lista" })).Status);
        return id;
    }

    [Fact]
    public async Task Catalogo_y_plantilla_del_sistema()
    {
        var (a, _) = await Escenario();
        var (_, v) = await a.Get("/api/contratos/variables");
        Assert.NotEmpty(v!["data"]!["variables"]!.AsArray());
        Assert.Contains(v["data"]!["motor"]!.GetValue<string>(), new[] { "libreoffice", "aproximado" });

        var (_, l) = await a.Get("/api/contratos/plantillas");
        var primera = l!["data"]![0]!;
        Assert.Equal("sistema", Str(primera, "id")); Assert.True(primera["predeterminada"]!.GetValue<bool>()); Assert.True(primera["sistema"]!.GetValue<bool>());
        Assert.StartsWith("PK", System.Text.Encoding.ASCII.GetString((await DocxSistema(a)).Take(2).ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Put("/api/contratos/plantillas/sistema", new { nombre = "X" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Delete("/api/contratos/plantillas/sistema")).Status);
    }

    [Fact]
    public async Task Plantilla_propia_se_sube_se_guarda_y_se_hace_predeterminada()
    {
        var (a, _) = await Escenario();
        var otro = await api.RegisterAdmin();
        var docx = await DocxSistema(a);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/contratos/plantillas", new { nombre = "Vacío" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/contratos/plantillas", new { nombre = "Roto", archivoBase64 = "!!!no-es-base64" })).Status);

        var (st, b) = await a.Post("/api/contratos/plantillas", new { nombre = "  Mi   contrato  ", archivoBase64 = Convert.ToBase64String(docx) });
        Assert.Equal(HttpStatusCode.Created, st);
        var id = Str(b, "data", "id");
        Assert.Equal("Mi contrato", Str(b, "data", "nombre"));
        Assert.NotEmpty(b!["data"]!["variables_presentes"]!.AsArray());
        Assert.False(string.IsNullOrEmpty(Str(b, "data", "html")));

        // borrador no puede ser predeterminada
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post($"/api/contratos/plantillas/{id}/predeterminada")).Status);
        Assert.Equal(HttpStatusCode.OK, (await a.Put($"/api/contratos/plantillas/{id}", new { estado = "lista", nombre = "Renombrada" })).Status);
        Assert.Equal(HttpStatusCode.OK, (await a.Post($"/api/contratos/plantillas/{id}/predeterminada")).Status);

        var (_, l) = await a.Get("/api/contratos/plantillas");
        Assert.Equal("Renombrada", l!["data"]![0]!["nombre"]!.GetValue<string>()); // la predeterminada va primero
        Assert.False(l["data"]!.AsArray().First(x => Str(x, "id") == "sistema")!["predeterminada"]!.GetValue<bool>());

        // aislamiento entre propietarios
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Get($"/api/contratos/plantillas/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Delete($"/api/contratos/plantillas/{id}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await a.Delete($"/api/contratos/plantillas/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Get($"/api/contratos/plantillas/{id}")).Status);
    }

    [Fact]
    public async Task Plantilla_se_asigna_a_propiedades()
    {
        var (a, _) = await Escenario();
        var id = await PlantillaPropia(a);
        var (_, deptos) = await a.Get("/api/departamentos");
        var deptoId = deptos!["data"]![0]!["id"]!.GetValue<int>();

        Assert.Equal(HttpStatusCode.OK, (await a.Put($"/api/contratos/plantillas/{id}/asignar", new { departamento_ids = new[] { deptoId } })).Status);
        var (_, l) = await a.Get("/api/contratos/plantillas");
        Assert.Equal(1, l!["data"]!.AsArray().First(x => Str(x, "id") == id)!["propiedades"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Put("/api/contratos/plantillas/sistema/asignar", new { departamento_ids = new[] { deptoId } })).Status);
        await a.Put($"/api/contratos/plantillas/{id}/asignar", new { departamento_ids = Array.Empty<int>() });
        Assert.Equal(0, (await a.Get("/api/contratos/plantillas")).Body!["data"]!.AsArray().First(x => Str(x, "id") == id)!["propiedades"]!.GetValue<int>());
    }

    [Fact]
    public async Task Preparar_generar_versiona_descarga_y_elimina()
    {
        var (a, inq) = await Escenario();
        var (_, prep) = await a.Get($"/api/contratos/inquilino/{inq}/preparar");
        Assert.Equal("sistema", Str(prep, "data", "plantilla_id")); Assert.Equal("sistema", Str(prep, "data", "origen"));
        Assert.Empty(prep!["data"]!["versiones"]!.AsArray());

        var (s1, g1) = await a.Post($"/api/contratos/inquilino/{inq}/generar", new { });
        Assert.Equal(HttpStatusCode.Created, s1);
        Assert.Equal(1, g1!["data"]!["version"]!.GetValue<int>());
        var (_, g2) = await a.Post($"/api/contratos/inquilino/{inq}/generar", new { });
        Assert.Equal(2, g2!["data"]!["version"]!.GetValue<int>());
        var id2 = Str(g2, "data", "id");

        var (_, hist) = await a.Get($"/api/contratos?inquilino_id={inq}");
        Assert.Equal(2, hist!["data"]!.AsArray().Count);
        var (_, res) = await a.Get("/api/contratos/resumen");
        var fila = res!["data"]!.AsArray().First(x => Str(x, "id") == inq)!;
        Assert.Equal(2, fila["version"]!.GetValue<int>()); Assert.False(fila["desactualizado"]!.GetValue<bool>());

        var pdf = await a.Http.GetAsync($"/api/contratos/{id2}/archivo?formato=pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync()).Take(4).ToArray()));
        var docx = await a.Http.GetAsync($"/api/contratos/{id2}/archivo?formato=docx");
        Assert.Contains("wordprocessingml", docx.Content.Headers.ContentType!.MediaType);

        var otro = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Get($"/api/contratos/{id2}/archivo")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Get("/api/contratos/no-es-guid/archivo")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Delete($"/api/contratos/{id2}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await a.Delete($"/api/contratos/{id2}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Delete($"/api/contratos/{id2}")).Status);
    }

    [Fact]
    public async Task Generar_puede_completar_datos_faltantes_del_inquilino()
    {
        var a = await api.RegisterAdmin();
        await a.Post("/api/departamentos", new { numero = 1 });
        var (_, b) = await a.Post("/api/inquilinos", new
        {
            nombre_completo = "Sin Domicilio", depto_numero = 1, renta = 3000, fecha_pago = "5",
            fecha_inicio = DateTime.Today.ToString("yyyy-MM-dd"), fecha_termino = DateTime.Today.AddMonths(6).ToString("yyyy-MM-dd"),
        });
        var inq = Str(b, "data", "id");
        var (st, _) = await a.Post($"/api/contratos/inquilino/{inq}/generar", new { completar = new { inquilino_domicilio = "  Av. Nueva 45  ", clave_inventada = "x" } });
        Assert.Equal(HttpStatusCode.Created, st);
        Assert.Equal("Av. Nueva 45", Str((await a.Get($"/api/inquilinos/{inq}")).Body, "data", "domicilio"));
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post($"/api/contratos/inquilino/{inq}/generar", new { completar = new { inquilino_domicilio = new string('x', 501) } })).Status);
    }

    [Fact]
    public async Task Generar_pendientes_crea_solo_los_que_no_tienen_contrato()
    {
        var (a, inq) = await Escenario();
        await a.Post("/api/departamentos", new { numero = 2 });
        await a.Post("/api/inquilinos", new
        {
            nombre_completo = "Segundo Inquilino", depto_numero = 2, renta = 3000, fecha_pago = "5",
            fecha_inicio = DateTime.Today.ToString("yyyy-MM-dd"), fecha_termino = DateTime.Today.AddMonths(6).ToString("yyyy-MM-dd"),
        });
        await a.Post($"/api/contratos/inquilino/{inq}/generar", new { });
        var (st, r) = await a.Post("/api/contratos/generar-pendientes", new { });
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.Equal(1, r!["data"]!["generados"]!.GetValue<int>());
        Assert.Empty(r["data"]!["fallos"]!.AsArray());
    }

    [Fact]
    public async Task Vista_previa_devuelve_un_pdf()
    {
        var (a, inq) = await Escenario();
        var res = await a.Http.PostAsJsonAsync("/api/contratos/plantillas/sistema/preview", new { inquilino_id = inq });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/pdf", res.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Contratos_respeta_permisos_y_propietario()
    {
        var (a, inq) = await Escenario();
        var sin = await api.InviteUser(a, "cobrador", new[] { "pagos" });
        var con = await api.InviteUser(a, "cobrador", new[] { "contratos", "inquilinos" });
        Assert.Equal(HttpStatusCode.Forbidden, (await sin.Get("/api/contratos/plantillas")).Status);
        Assert.Equal(HttpStatusCode.OK, (await con.Get("/api/contratos/plantillas")).Status);
        Assert.Equal(HttpStatusCode.Created, (await con.Post($"/api/contratos/inquilino/{inq}/generar", new { })).Status); // genera
        var docx = await DocxSistema(a);
        Assert.Equal(HttpStatusCode.Forbidden, (await con.Post("/api/contratos/plantillas", new { nombre = "X", archivoBase64 = Convert.ToBase64String(docx) })).Status); // subir plantillas: solo admin
        var otro = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Post($"/api/contratos/inquilino/{inq}/generar", new { })).Status);
    }
}
