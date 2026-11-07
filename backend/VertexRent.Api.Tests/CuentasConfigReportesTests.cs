using System.Net;
using System.Text.Json.Nodes;

namespace VertexRent.Api.Tests;

/// <summary>Fija el comportamiento de cuentas bancarias, configuración y reportes (red de seguridad para la reestructura).</summary>
[Collection("api")]
public sealed class CuentasConfigReportesTests(ApiFixture api)
{
    const string Clabe = "012345678901234567";
    static string Str(JsonNode? n, params string[] path) { foreach (var p in path) n = n?[p]; return n!.GetValue<string>(); }
    static decimal D(JsonNode? n) => n switch
    {
        null => 0m,
        JsonValue v when v.TryGetValue<string>(out var s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
        JsonValue v => v.GetValue<decimal>(),
        _ => 0m,
    };

    // ── Cuentas bancarias ────────────────────────────────────────────────────
    [Fact]
    public async Task Cuenta_valida_clabe_y_la_primera_es_la_predeterminada()
    {
        var a = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/cuentas-bancarias", new { banco_clabe = "123" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Post("/api/cuentas-bancarias", new { alias = "Sin clabe" })).Status);
        var (s1, c1) = await a.Post("/api/cuentas-bancarias", new { banco_clabe = Clabe, banco_nombre = " BBVA ", banco_titular = "Don Titular", alias = "Principal" });
        Assert.Equal(HttpStatusCode.Created, s1);
        Assert.True(c1!["data"]!["es_predeterminada"]!.GetValue<bool>());
        Assert.Equal("BBVA", Str(c1, "data", "banco_nombre"));
        var (_, c2) = await a.Post("/api/cuentas-bancarias", new { banco_clabe = "999999999999999999" });
        Assert.False(c2!["data"]!["es_predeterminada"]!.GetValue<bool>());
        var (_, lista) = await a.Get("/api/cuentas-bancarias");
        Assert.Equal(Str(c1, "data", "id"), lista!["data"]![0]!["id"]!.GetValue<string>()); // la predeterminada va primero
        Assert.Equal(2, lista!["data"]!.AsArray().Count);
    }

    [Fact]
    public async Task Cuenta_se_edita_parcialmente_y_cambia_la_predeterminada()
    {
        var a = await api.RegisterAdmin(); var b = await api.RegisterAdmin();
        var (_, c1) = await a.Post("/api/cuentas-bancarias", new { banco_clabe = Clabe, alias = "Uno", banco_nombre = "BBVA" });
        var (_, c2) = await a.Post("/api/cuentas-bancarias", new { banco_clabe = "999999999999999999", alias = "Dos" });
        var id1 = Str(c1, "data", "id"); var id2 = Str(c2, "data", "id");

        var (st, u) = await a.Put($"/api/cuentas-bancarias/{id1}", new { alias = "Renombrada" });
        Assert.Equal(HttpStatusCode.OK, st);
        Assert.Equal("Renombrada", Str(u, "data", "alias")); Assert.Equal("BBVA", Str(u, "data", "banco_nombre")); Assert.Equal(Clabe, Str(u, "data", "banco_clabe"));
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Put($"/api/cuentas-bancarias/{id1}", new { banco_clabe = "abc" })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Put($"/api/cuentas-bancarias/{id1}", new { alias = "X" })).Status);

        Assert.Equal(HttpStatusCode.OK, (await a.Patch($"/api/cuentas-bancarias/{id2}/predeterminada")).Status);
        var lista = (await a.Get("/api/cuentas-bancarias")).Body!["data"]!.AsArray();
        Assert.Equal(id2, lista[0]!["id"]!.GetValue<string>());
        Assert.Single(lista, x => x!["es_predeterminada"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.NotFound, (await b.Patch($"/api/cuentas-bancarias/{id1}/predeterminada")).Status);
    }

    [Fact]
    public async Task Cuenta_eliminada_promueve_otra_y_la_ultima_pasa_inquilinos_a_efectivo()
    {
        var a = await api.RegisterAdmin(); var b = await api.RegisterAdmin();
        await a.Post("/api/departamentos", new { numero = 1 });
        await a.Post("/api/inquilinos", new { nombre_completo = "Transfer Uno", depto_numero = 1, renta = 1000, fecha_pago = "5", metodo_pago = "transferencia",
            fecha_inicio = DateTime.Today.ToString("yyyy-MM-dd"), fecha_termino = DateTime.Today.AddMonths(6).ToString("yyyy-MM-dd") });
        var (_, c1) = await a.Post("/api/cuentas-bancarias", new { banco_clabe = Clabe });
        var (_, c2) = await a.Post("/api/cuentas-bancarias", new { banco_clabe = "999999999999999999" });
        var id1 = Str(c1, "data", "id"); var id2 = Str(c2, "data", "id");

        Assert.Equal(HttpStatusCode.NotFound, (await b.Delete($"/api/cuentas-bancarias/{id1}")).Status);
        var (_, d1) = await a.Delete($"/api/cuentas-bancarias/{id1}"); // la predeterminada
        Assert.Equal(0, d1!["inquilinos_a_efectivo"]!.GetValue<int>());
        Assert.True((await a.Get("/api/cuentas-bancarias")).Body!["data"]![0]!["es_predeterminada"]!.GetValue<bool>()); // se promovió la otra

        var (_, d2) = await a.Delete($"/api/cuentas-bancarias/{id2}"); // la última
        Assert.Equal(1, d2!["inquilinos_a_efectivo"]!.GetValue<int>());
        Assert.Equal("efectivo", (await a.Get("/api/inquilinos")).Body!["data"]![0]!["metodo_pago"]!.GetValue<string>());
    }

    [Fact]
    public async Task Cuenta_se_asigna_a_departamento_y_la_config_la_resuelve()
    {
        var a = await api.RegisterAdmin();
        await a.Post("/api/departamentos", new { numero = 1 });
        var (_, c1) = await a.Post("/api/cuentas-bancarias", new { banco_clabe = Clabe, banco_nombre = "Predeterminado" });
        var (_, c2) = await a.Post("/api/cuentas-bancarias", new { banco_clabe = "999999999999999999", banco_nombre = "Del depto 1" });
        Assert.Equal("Predeterminado", Str((await a.Get("/api/config?depto_numero=1")).Body, "data", "banco_nombre"));

        Assert.Equal(HttpStatusCode.OK, (await a.Patch("/api/cuentas-bancarias/departamento/1", new { cuenta_bancaria_id = Str(c2, "data", "id") })).Status);
        Assert.Equal("Del depto 1", Str((await a.Get("/api/config?depto_numero=1")).Body, "data", "banco_nombre"));
        var (_, lista) = await a.Get("/api/cuentas-bancarias");
        var conDepto = lista!["data"]!.AsArray().First(x => Str(x, "id") == Str(c2, "data", "id"))!;
        Assert.Equal(1, conDepto["departamentos"]![0]!.GetValue<int>());

        Assert.Equal(HttpStatusCode.OK, (await a.Patch("/api/cuentas-bancarias/departamento/1", new { cuenta_bancaria_id = (string?)null })).Status);
        Assert.Equal("Predeterminado", Str((await a.Get("/api/config?depto_numero=1")).Body, "data", "banco_nombre"));
        Assert.Equal(HttpStatusCode.NotFound, (await a.Patch("/api/cuentas-bancarias/departamento/99", new { cuenta_bancaria_id = (string?)null })).Status);
        var otro = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.NotFound, (await otro.Patch("/api/cuentas-bancarias/departamento/1", new { cuenta_bancaria_id = Str(c2, "data", "id") })).Status);
    }

    [Fact]
    public async Task Cuentas_respetan_permisos()
    {
        var a = await api.RegisterAdmin();
        await a.Post("/api/cuentas-bancarias", new { banco_clabe = Clabe });
        var con = await api.InviteUser(a, "cobrador", new[] { "cuentas" });
        var sin = await api.InviteUser(a, "cobrador", new[] { "pagos" });
        Assert.Single((await con.Get("/api/cuentas-bancarias")).Body!["data"]!.AsArray()); // ve las de su propietario
        Assert.Equal(HttpStatusCode.Forbidden, (await sin.Get("/api/cuentas-bancarias")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await con.Post("/api/cuentas-bancarias", new { banco_clabe = Clabe })).Status);
    }

    // ── Configuración ────────────────────────────────────────────────────────
    [Fact]
    public async Task Config_guarda_solo_claves_permitidas_y_las_valida()
    {
        var a = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.OK, (await a.Put("/api/config", new { arrendador_nombre = "Don Arrendador", clave_inventada = "x", dias_gracia_retraso = 3 })).Status);
        var cfg = (await a.Get("/api/config")).Body!["data"]!;
        Assert.Equal("Don Arrendador", cfg["arrendador_nombre"]!.GetValue<string>());
        Assert.Equal("3", cfg["dias_gracia_retraso"]!.GetValue<string>());
        Assert.Null(cfg["clave_inventada"]);

        Assert.Equal(HttpStatusCode.BadRequest, (await a.Put("/api/config", new { dias_gracia_retraso = 91 })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Put("/api/config", new { dias_gracia_retraso = "mucho" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Put("/api/config", new { recordatorios_renta_dias = "3,99" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Put("/api/config", new { recordatorios_renta_dias = "1,2,3,4,5,6,7" })).Status);

        await a.Put("/api/config", new { recordatorios_renta_dias = "1, 3, 3, 0" });
        Assert.Equal("3,1,0", (await a.Get("/api/config")).Body!["data"]!["recordatorios_renta_dias"]!.GetValue<string>()); // únicos y descendentes
        await a.Put("/api/config", new { recordatorios_renta_dias = "" });
        Assert.Equal("", (await a.Get("/api/config")).Body!["data"]!["recordatorios_renta_dias"]!.GetValue<string>());
    }

    [Fact]
    public async Task Config_es_por_propietario_y_solo_el_admin_la_edita()
    {
        var a = await api.RegisterAdmin(); var b = await api.RegisterAdmin();
        await a.Put("/api/config", new { arrendador_nombre = "De A" });
        var colab = await api.InviteUser(a, "cobrador", new[] { "pagos" });
        Assert.Equal("De A", Str((await colab.Get("/api/config")).Body, "data", "arrendador_nombre")); // el colaborador lee la del propietario
        Assert.Equal(HttpStatusCode.Forbidden, (await colab.Put("/api/config", new { arrendador_nombre = "X" })).Status);
        Assert.Null((await b.Get("/api/config")).Body!["data"]!["arrendador_nombre"]);
    }

    // ── Reportes ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Reportes_suman_renta_cuotas_y_deposito_por_mes_y_departamento()
    {
        var a = await api.RegisterAdmin();
        await a.Post("/api/departamentos", new { numero = 1 });
        var (_, inq) = await a.Post("/api/inquilinos", new { nombre_completo = "Reporte Uno", depto_numero = 1, renta = 3000, deposito = 1000, fecha_pago = "5",
            fecha_inicio = DateTime.Today.ToString("yyyy-MM-dd"), fecha_termino = DateTime.Today.AddMonths(6).ToString("yyyy-MM-dd") });
        var id = Str(inq, "data", "id");
        await a.Post("/api/pagos/abono", new { inquilino_id = id, monto = 1200 });
        await a.Post("/api/depositos/abono", new { inquilino_id = id, monto = 400 });
        var (_, cu) = await a.Post("/api/cuotas", new { inquilino_id = id, concepto = "Agua", monto = 150 });
        await a.Put($"/api/cuotas/{Str(cu, "data", "id")}/pagar");

        var year = DateTime.Now.Year;
        var (_, anual) = await a.Get($"/api/reportes/anual?year={year}");
        Assert.Equal(1200m, D(anual!["data"]!["renta_total"])); Assert.Equal(150m, D(anual["data"]!["extra_total"]));
        Assert.Equal(400m, D(anual["data"]!["deposito_total"])); Assert.Equal(1750m, D(anual["data"]!["total_general"]));

        var (_, mensual) = await a.Get($"/api/reportes/mensual?year={year}");
        var m = mensual!["data"]!;
        Assert.Equal(12, m["meses"]!.AsArray().Count);
        var mes = m["meses"]![DateTime.Now.Month - 1]!;
        Assert.Equal(1200m, D(mes["renta"])); Assert.Equal(150m, D(mes["extra"])); Assert.Equal(400m, D(mes["deposito"])); Assert.Equal(1750m, D(mes["total"]));
        Assert.Equal(1750m, D(m["total_anual"]));
        Assert.Equal(1, m["por_departamento"]![0]!["depto_numero"]!.GetValue<int>());
        Assert.Equal(1750m, D(m["por_departamento"]![0]!["total"]));
        Assert.Contains(year, m["anios_disponibles"]!.AsArray().Select(x => x!.GetValue<int>()));
    }

    [Fact]
    public async Task Reportes_validan_el_anio_aislan_propietarios_y_exigen_permiso()
    {
        var a = await api.RegisterAdmin(); var b = await api.RegisterAdmin();
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Get("/api/reportes/anual")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Get("/api/reportes/anual?year=1999")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Get("/api/reportes/mensual?year=abc")).Status);
        var inq = await api.CreateInquilino(a, 1);
        await a.Post("/api/pagos/abono", new { inquilino_id = inq, monto = 500 });
        Assert.Equal(0m, D((await b.Get($"/api/reportes/anual?year={DateTime.Now.Year}")).Body!["data"]!["total_general"]));
        var sin = await api.InviteUser(a, "cobrador", new[] { "pagos" });
        var con = await api.InviteUser(a, "cobrador", new[] { "reportes" });
        Assert.Equal(HttpStatusCode.Forbidden, (await sin.Get($"/api/reportes/anual?year={DateTime.Now.Year}")).Status);
        Assert.Equal(500m, D((await con.Get($"/api/reportes/anual?year={DateTime.Now.Year}")).Body!["data"]!["total_general"])); // el colaborador ve los del propietario
    }
}
