using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Cronos;

namespace VertexRent.Api;

/// <summary>Recordatorios diarios (9:00 AM CDMX): renta, vencimiento de contrato, depósitos diferidos y promesas de pago.</summary>
public sealed class SchedulerService : BackgroundService
{
    static readonly TimeSpan MaxEspera = TimeSpan.FromHours(6);
    static readonly int[] DefaultRecordatoriosRenta = { 1, 0 };

    /// <summary>Ejecuta las cuatro comprobaciones (lo que hace el programador cada día a las 9:00).</summary>
    internal static Task EjecutarTodas() =>
        Task.WhenAll(CheckRentaReminders(), CheckContratoExpiration(), CheckDepositoReminders(), CheckPromesaPagoReminders());

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var cron = CronExpression.Parse("0 9 * * *");
        Console.WriteLine("✅ Scheduler de notificaciones configurado (diario 9:00 AM)");

        while (!ct.IsCancellationRequested)
        {
            var next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, Js.Cdmx);
            if (next == null) return;
            var delay = next.Value - DateTimeOffset.UtcNow;
            // Task.Delay no admite más de ~49 días: se espera por tramos y se vuelve a calcular.
            var tramo = delay > MaxEspera ? MaxEspera : (delay > TimeSpan.Zero ? delay : TimeSpan.Zero);
            try { await Task.Delay(tramo, ct); }
            catch (OperationCanceledException) { return; }
            if (DateTimeOffset.UtcNow < next.Value) continue;

            Console.WriteLine("[Scheduler] Ejecutando recordatorios de pago y contratos...");
            await EjecutarTodas();
            Console.WriteLine("[Scheduler] Recordatorios enviados.");
            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); } catch (OperationCanceledException) { return; }
        }
    }

    static int? ParseDiaPago(string? fechaPago)
    {
        var m = Regex.Match(fechaPago ?? "", @"^(\d{1,2})");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    static int[] ParseRecordatorioDias(string? valor)
    {
        if (valor == null) return DefaultRecordatoriosRenta;
        if (valor.Trim() == "") return Array.Empty<int>();
        return valor.Split(',')
            .Select(s => Js.ParseInt(s.Trim()))
            .Where(n => n is >= 0 and <= 30).Select(n => n!.Value).Distinct().ToArray();
    }

    static async Task<Dictionary<string, int[]>> GetRecordatoriosRentaPorAdmin()
    {
        var map = new Dictionary<string, int[]>();
        foreach (var row in await RecordatoriosRepository.ConfigRecordatoriosRenta()) map[row.S("admin_id")!] = ParseRecordatorioDias(row.S("valor"));
        return map;
    }

    static int DiasHastaFecha(int targetDay)
    {
        var hoy = DateTime.Now.Date;
        // Igual que new Date(anio, mes, dia): un día mayor al del mes desborda al siguiente.
        var target = new DateTime(hoy.Year, hoy.Month, 1).AddDays(targetDay - 1);
        if (target < hoy) target = new DateTime(hoy.Year, hoy.Month, 1).AddMonths(1).AddDays(targetDay - 1);
        return (int)Math.Round((target - hoy).TotalDays);
    }

    static async Task<bool> YaNotificadoHoy(string usuarioId, string tipo, string subKey)
    {
        return await RecordatoriosRepository.YaNotificadoHoy(usuarioId, tipo, subKey);
    }

    internal static async Task CheckRentaReminders()
    {
        try
        {
            var porAdmin = await GetRecordatoriosRentaPorAdmin();
            var inquilinos = await RecordatoriosRepository.InquilinosActivosParaRenta();

            foreach (var inq in inquilinos)
            {
                var diaPago = ParseDiaPago(inq.S("fecha_pago"));
                if (diaPago is null or 0) continue;

                var adminId = inq.S("admin_id");
                var objetivo = adminId != null && porAdmin.TryGetValue(adminId, out var d) ? d : DefaultRecordatoriosRenta;
                if (objetivo.Length == 0) continue;

                var restantes = DiasHastaFecha(diaPago.Value);
                if (!objetivo.Contains(restantes)) continue;

                var subKey = $"pago_dia_{diaPago}_dias_{restantes}";
                var depto = inq.S("depto_numero");

                var usuarioId = inq.S("usuario_id");
                if (!string.IsNullOrEmpty(usuarioId) && !await YaNotificadoHoy(usuarioId, "renta", subKey))
                {
                    var titulo = restantes == 0 ? "🔔 Hoy es tu día de pago" : "📅 Recordatorio de pago";
                    var msg = restantes == 0 ? $"Hoy es tu día de pago — Recuerda pagar tu renta del depto {depto}"
                        : restantes == 1 ? $"Tu pago de renta vence mañana — Depto {depto}"
                        : $"Tu pago de renta vence en {restantes} días — Depto {depto}";
                    await PushService.CreateAndSendNotification(usuarioId, titulo, msg, "renta", subKey);
                }

                if (!string.IsNullOrEmpty(adminId) && !await YaNotificadoHoy(adminId, "renta", $"admin_{subKey}_{inq.S("id")}"))
                {
                    var nombre = inq.S("nombre_completo");
                    var adminMsg = restantes == 0 ? $"Hoy vence el pago de {nombre} (Depto {depto})"
                        : restantes == 1 ? $"Mañana vence el pago de {nombre} (Depto {depto})"
                        : $"En {restantes} días vence el pago de {nombre} (Depto {depto})";
                    await PushService.NotificarEquipoPagos(adminId, "💰 Vencimiento de renta próximo", adminMsg, "renta", $"admin_{subKey}_{inq.S("id")}");
                }
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Scheduler] Error en checkRentaReminders: {ex}"); }
    }

    internal static async Task CheckContratoExpiration()
    {
        try
        {
            var objetivo = new[] { 30, 7, 1 };
            var maxDias = objetivo.Max();
            var inquilinos = await RecordatoriosRepository.ContratosPorVencer(maxDias + 1);

            foreach (var inq in inquilinos)
            {
                var termino = inq.Dt("fecha_termino")!.Value.ToLocalTime();
                var restantes = (int)Math.Round((termino - DateTime.Now.Date).TotalDays);
                if (!objetivo.Contains(restantes)) continue;

                var subKey = $"contrato_{inq.S("id")}_dias_{restantes}";
                var depto = inq.S("depto_numero"); var nombre = inq.S("nombre_completo");

                var usuarioId = inq.S("usuario_id");
                if (!string.IsNullOrEmpty(usuarioId) && !await YaNotificadoHoy(usuarioId, "renta", subKey))
                {
                    var msg = restantes == 1 ? $"Tu contrato vence mañana — Depto {depto}" : $"Tu contrato vence en {restantes} días — Depto {depto}";
                    await PushService.CreateAndSendNotification(usuarioId, "📋 Tu contrato está por vencer", msg, "renta", subKey);
                }

                var adminId = inq.S("admin_id");
                if (!string.IsNullOrEmpty(adminId) && !await YaNotificadoHoy(adminId, "renta", $"admin_{subKey}"))
                {
                    var adminMsg = restantes == 1 ? $"El contrato de {nombre} (Depto {depto}) vence mañana"
                        : $"El contrato de {nombre} (Depto {depto}) vence en {restantes} días";
                    await PushService.NotificarEquipoPagos(adminId, "📋 Contrato por vencer", adminMsg, "renta", $"admin_{subKey}");
                }
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Scheduler] Error en checkContratoExpiration: {ex}"); }
    }

    internal static async Task CheckDepositoReminders()
    {
        try
        {
            var objetivo = new[] { 3, 1, 0 };
            var inquilinos = await RecordatoriosRepository.InquilinosConDepositoDiferido();

            foreach (var inq in inquilinos)
            {
                if (inq.J("deposito_fechas") is not JsonArray fechas) continue;
                var hoy = DateTime.Now.Date;

                foreach (var f in fechas)
                {
                    var fechaStr = f is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
                    if (fechaStr == null || !Regex.IsMatch(fechaStr, @"^\d{4}-\d{2}-\d{2}$")) continue;

                    var target = DateTime.Parse(fechaStr + "T12:00:00", System.Globalization.CultureInfo.InvariantCulture).Date;
                    var restantes = (int)Math.Round((target - hoy).TotalDays);
                    if (!objetivo.Contains(restantes)) continue;

                    var subKey = $"deposito_{inq.S("id")}_{fechaStr}_dias_{restantes}";
                    var depto = inq.S("depto_numero"); var nombre = inq.S("nombre_completo");

                    var usuarioId = inq.S("usuario_id");
                    if (!string.IsNullOrEmpty(usuarioId) && !await YaNotificadoHoy(usuarioId, "renta", subKey))
                    {
                        var titulo = restantes == 0 ? "🔔 Hoy es el pago de tu depósito diferido" : "📅 Recordatorio de depósito diferido";
                        var msg = restantes == 0 ? $"Hoy vence el pago de tu depósito diferido — Depto {depto}"
                            : restantes == 1 ? $"Tu pago de depósito diferido vence mañana — Depto {depto}"
                            : $"Tu pago de depósito diferido vence en {restantes} días — Depto {depto}";
                        await PushService.CreateAndSendNotification(usuarioId, titulo, msg, "renta", subKey);
                    }

                    var adminId = inq.S("admin_id");
                    if (!string.IsNullOrEmpty(adminId) && !await YaNotificadoHoy(adminId, "renta", $"admin_{subKey}"))
                    {
                        var adminMsg = restantes == 0 ? $"Hoy vence el depósito diferido de {nombre} (Depto {depto})"
                            : restantes == 1 ? $"Mañana vence el depósito diferido de {nombre} (Depto {depto})"
                            : $"En {restantes} días vence el depósito diferido de {nombre} (Depto {depto})";
                        await PushService.NotificarEquipoPagos(adminId, "💰 Vencimiento de depósito próximo", adminMsg, "renta", $"admin_{subKey}");
                    }
                }
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Scheduler] Error en checkDepositoReminders: {ex}"); }
    }

    internal static async Task CheckPromesaPagoReminders()
    {
        try
        {
            var objetivo = new[] { 1, 0 };
            var pagos = await RecordatoriosRepository.PromesasPendientes();
            var hoy = DateTime.Now.Date;

            foreach (var pago in pagos)
            {
                var adminId = pago.S("admin_id");
                if (string.IsNullOrEmpty(adminId)) continue;

                var fechaIso = pago.S("fecha_promesa")![..10];
                var target = DateTime.Parse(fechaIso + "T12:00:00", System.Globalization.CultureInfo.InvariantCulture).Date;
                var restantes = (int)Math.Round((target - hoy).TotalDays);
                if (!objetivo.Contains(restantes)) continue;

                var subKey = $"promesa_{pago.S("id")}_dias_{restantes}";
                if (await YaNotificadoHoy(adminId, "promesa", subKey)) continue;

                var nombre = pago.S("nombre_completo"); var depto = pago.S("depto_numero"); var periodo = pago.S("periodo");
                var titulo = restantes == 0 ? "🤝 Hoy prometió pagar" : "🤝 Recordatorio de promesa de pago";
                var msg = restantes == 0
                    ? $"Hoy es la fecha en que {nombre} (Depto {depto}) dijo que pagaría {periodo}"
                    : $"Mañana es la fecha en que {nombre} (Depto {depto}) dijo que pagaría {periodo}";
                await PushService.NotificarEquipoPagos(adminId, titulo, msg, "promesa", subKey);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Scheduler] Error en checkPromesaPagoReminders: {ex}"); }
    }
}
