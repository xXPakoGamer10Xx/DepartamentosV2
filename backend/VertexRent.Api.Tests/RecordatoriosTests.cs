using System.Net;
using System.Text.Json.Nodes;

namespace VertexRent.Api.Tests;

/// <summary>Ejecuta directamente las comprobaciones diarias del programador y verifica a quién avisan.</summary>
[Collection("api")]
public sealed class RecordatoriosTests(ApiFixture api)
{
    static string Hoy => DateTime.Now.ToString("yyyy-MM-dd");

    /// <summary>Admin + inquilino vinculado a una cuenta, con los campos dados.</summary>
    async Task<(ApiFixture.Session A, ApiFixture.Session Inq, string Id)> Escenario(object campos)
    {
        var a = await api.RegisterAdmin();
        await a.Post("/api/departamentos", new { numero = 1 });
        var (st, b) = await a.Post("/api/inquilinos", campos);
        Assert.Equal(HttpStatusCode.Created, st);
        var id = b!["data"]!["id"]!.GetValue<string>();
        var cuenta = await api.InviteUser(a, "inquilino");
        await a.Put($"/api/inquilinos/{id}/vincular-usuario", new { usuario_id = cuenta.Id });
        return (a, cuenta, id);
    }

    static async Task<List<JsonNode>> Notas(ApiFixture.Session s) =>
        (await s.Get("/api/notificaciones")).Body!["data"]!.AsArray().Select(n => n!).ToList();

    static object Base(string fechaPago, string? termino = null, object? depositoFechas = null) => new
    {
        nombre_completo = "Recordatorio Prueba", depto_numero = 1, renta = 3000, fecha_pago = fechaPago,
        fecha_inicio = DateTime.Today.AddMonths(-1).ToString("yyyy-MM-dd"),
        fecha_termino = termino ?? DateTime.Today.AddMonths(12).ToString("yyyy-MM-dd"),
        deposito_fechas = depositoFechas ?? Array.Empty<string>(),
    };

    [Fact]
    public async Task Renta_el_dia_de_pago_avisa_al_inquilino_y_al_admin_sin_duplicar()
    {
        var (a, inq, _) = await Escenario(Base(DateTime.Now.Day.ToString()));
        await SchedulerService.CheckRentaReminders();
        Assert.Contains(await Notas(inq), n => n["titulo"]!.GetValue<string>().Contains("día de pago"));
        Assert.Contains(await Notas(a), n => n["tipo"]!.GetValue<string>() == "renta" && n["mensaje"]!.GetValue<string>().Contains("Hoy vence el pago"));

        await SchedulerService.CheckRentaReminders();   // segunda pasada el mismo día
        Assert.Single(await Notas(inq), n => n["tipo"]!.GetValue<string>() == "renta");
        Assert.Single(await Notas(a), n => n["tipo"]!.GetValue<string>() == "renta");
    }

    [Fact]
    public async Task Renta_un_dia_antes_avisa_que_vence_manana()
    {
        var (_, inq, _) = await Escenario(Base(DateTime.Now.AddDays(1).Day.ToString()));
        await SchedulerService.CheckRentaReminders();
        Assert.Contains(await Notas(inq), n => n["mensaje"]!.GetValue<string>().Contains("vence mañana"));
    }

    [Fact]
    public async Task Renta_respeta_los_dias_configurados_por_el_admin()
    {
        var (a, inq, _) = await Escenario(Base(DateTime.Now.Day.ToString()));
        await a.Put("/api/config", new { recordatorios_renta_dias = "" });   // sin recordatorios
        await SchedulerService.CheckRentaReminders();
        Assert.Empty(await Notas(inq));
        Assert.Empty(await Notas(a));
    }

    [Fact]
    public async Task Contrato_por_vencer_avisa_a_los_7_dias_y_no_a_los_20()
    {
        var (a, inq, _) = await Escenario(Base("1", DateTime.Today.AddDays(7).ToString("yyyy-MM-dd")));
        var (a2, inq2, _) = await Escenario(Base("1", DateTime.Today.AddDays(20).ToString("yyyy-MM-dd")));
        await SchedulerService.CheckContratoExpiration();
        Assert.Contains(await Notas(inq), n => n["titulo"]!.GetValue<string>().Contains("por vencer") && n["mensaje"]!.GetValue<string>().Contains("7 días"));
        Assert.Contains(await Notas(a), n => n["mensaje"]!.GetValue<string>().Contains("vence en 7 días"));
        Assert.Empty(await Notas(inq2)); Assert.Empty(await Notas(a2));
    }

    [Fact]
    public async Task Deposito_diferido_avisa_el_dia_de_la_fecha()
    {
        var (a, inq, _) = await Escenario(Base("1", depositoFechas: new[] { Hoy }));
        await SchedulerService.CheckDepositoReminders();
        Assert.Contains(await Notas(inq), n => n["titulo"]!.GetValue<string>().Contains("depósito diferido"));
        Assert.Contains(await Notas(a), n => n["mensaje"]!.GetValue<string>().Contains("depósito diferido"));
        await SchedulerService.CheckDepositoReminders();
        Assert.Single(await Notas(inq), n => n["titulo"]!.GetValue<string>().Contains("depósito"));
    }

    [Fact]
    public async Task Promesa_de_pago_avisa_al_equipo_el_dia_prometido()
    {
        var (a, _, id) = await Escenario(Base("1"));
        var (st, _) = await a.Put($"/api/pagos/promesa/inquilino/{id}", new { fecha_promesa = Hoy });
        Assert.Equal(HttpStatusCode.OK, st);
        await SchedulerService.CheckPromesaPagoReminders();
        Assert.Contains(await Notas(a), n => n["tipo"]!.GetValue<string>() == "promesa" && n["titulo"]!.GetValue<string>().Contains("Hoy prometió pagar"));
        await SchedulerService.CheckPromesaPagoReminders();
        Assert.Single(await Notas(a), n => n["tipo"]!.GetValue<string>() == "promesa");
    }

    [Fact]
    public async Task Promesa_de_un_pago_ya_confirmado_no_avisa()
    {
        var (a, _, id) = await Escenario(Base("1"));
        await a.Put($"/api/pagos/promesa/inquilino/{id}", new { fecha_promesa = Hoy });
        await a.Post($"/api/pagos/marcar-pagado/{id}");
        await SchedulerService.CheckPromesaPagoReminders();
        Assert.DoesNotContain(await Notas(a), n => n["tipo"]!.GetValue<string>() == "promesa");
    }
}
