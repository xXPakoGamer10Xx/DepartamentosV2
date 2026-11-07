using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using WebPush;

namespace VertexRent.Api;

public static class PushService
{
    static readonly string? VapidPublic = Env.Get("VAPID_PUBLIC_KEY");
    static readonly string? VapidPrivate = Env.Get("VAPID_PRIVATE_KEY");
    static readonly string VapidSubject = Env.Get("VAPID_SUBJECT", "mailto:soporte@vertexrent.app");
    static readonly WebPushClient WebClient = new();

    public sealed record WebSub(string Endpoint, string P256dh, string Auth);

    public static async Task<List<WebSub>> GetUserWebPushSubscriptions(string usuarioId)
    {
        return (await NotificacionesRepository.Suscripciones(usuarioId)).Select(x => new WebSub(x.S("endpoint")!, x.S("p256dh")!, x.S("auth")!)).ToList();
    }

    public static async Task SendWebPush(List<WebSub> subs, string title, string body, object? data = null)
    {
        if (string.IsNullOrEmpty(VapidPublic) || string.IsNullOrEmpty(VapidPrivate))
        {
            Console.Error.WriteLine("[web-push] VAPID_PUBLIC_KEY/VAPID_PRIVATE_KEY no configuradas — no se puede enviar");
            return;
        }
        if (subs.Count == 0) return;

        var payload = JsonSerializer.Serialize(new { title, body, data = data ?? new { } }, Api.JsonOpts);
        var vapid = new VapidDetails(VapidSubject, VapidPublic, VapidPrivate);
        var stale = new List<string>();

        await Task.WhenAll(subs.Select(async sub =>
        {
            try
            {
                await WebClient.SendNotificationAsync(new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth), payload, vapid);
            }
            catch (WebPushException ex)
            {
                // 404/410 = el navegador invalidó la suscripción
                if ((int)ex.StatusCode is 404 or 410) lock (stale) stale.Add(sub.Endpoint);
                else Console.Error.WriteLine($"[web-push] Error al enviar a {sub.Endpoint}: {(int)ex.StatusCode} {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[web-push] Error al enviar a {sub.Endpoint}: {ex.Message}");
            }
        }));

        if (stale.Count > 0) await NotificacionesRepository.EliminarSuscripciones(stale);
    }

    public static async Task CreateAndSendNotification(string usuarioId, string title, string body, string tipo, string? dedupeKey = null)
    {
        try
        {
            // 1. Guardar en BD (panel histórico web)
            await NotificacionesRepository.Insertar(usuarioId, title, body, tipo, dedupeKey);

            // 2. SSE si está conectado en web
            SseService.EmitToUser(usuarioId, "notification_new", new { title, mensaje = body, tipo });

            // 3. Web Push (navegador, incluso con la pestaña cerrada)
            var subs = await GetUserWebPushSubscriptions(usuarioId);
            if (subs.Count > 0) await SendWebPush(subs, title, body, new { tipo });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error al guardar/enviar notificación: {ex.Message}");
        }
    }

    /// <summary>Notificación de cobranza: al admin Y a sus colaboradores con permiso 'notificaciones.pagos'.</summary>
    public static async Task NotificarEquipoPagos(string adminId, string title, string body, string tipo = "pago", string? dedupeKey = null)
    {
        try { await CreateAndSendNotification(adminId, title, body, tipo, dedupeKey); } catch (Exception ex) { Console.Error.WriteLine($"Error al notificar al admin: {ex.Message}"); }
        try
        {
            foreach (var id in await NotificacionesRepository.ColaboradoresDeCobranza(adminId))
            {
                try { await CreateAndSendNotification(id, title, body, tipo, dedupeKey); } catch (Exception ex) { Console.Error.WriteLine($"Error al notificar a un colaborador: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error al notificar al equipo de pagos: {ex.Message}");
        }
    }
}
