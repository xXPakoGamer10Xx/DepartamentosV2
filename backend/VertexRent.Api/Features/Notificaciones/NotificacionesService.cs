using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Bandeja de notificaciones del usuario y alta de suscripciones push. El SQL vive en <see cref="NotificacionesRepository"/>.</summary>
public static class NotificacionesService
{
    /// <summary>Las notificaciones son de quien inició sesión (Actor), no del admin dueño.</summary>
    public static async Task<List<Row>> Listar(AuthUser u)
    {
        await NotificacionesRepository.LimpiarLeidasAntiguas(u.Actor);   // las leídas se limpian solas pasados 7 días
        return await NotificacionesRepository.Recientes(u.Actor);
    }

    public static Task MarcarTodasLeidas(AuthUser u) => NotificacionesRepository.MarcarTodasLeidas(u.Actor);
    public static Task EliminarLeidas(AuthUser u) => NotificacionesRepository.EliminarLeidas(u.Actor);
    public static Task Eliminar(AuthUser u, string id) => NotificacionesRepository.Eliminar(id, u.Actor);

    public static async Task<Row> MarcarLeida(AuthUser u, string id) =>
        await NotificacionesRepository.MarcarLeida(id, u.Actor) ?? throw new AppError("Notificación no encontrada o no pertenece al usuario", 404);

    public static async Task Suscribir(AuthUser u, JsonObject b)
    {
        var keys = b["keys"] as JsonObject;
        if (!Js.T(b["endpoint"]) || !Js.T(keys?["p256dh"]) || !Js.T(keys?["auth"])) throw new AppError("Suscripción inválida", 400);
        await NotificacionesRepository.GuardarSuscripcion(u.Actor, b.S("endpoint"), keys!.S("p256dh"), keys.S("auth"));
    }
}
