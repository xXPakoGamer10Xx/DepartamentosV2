namespace VertexRent.Api;

/// <summary>Rutas de notificaciones: enlazan la petición con <see cref="NotificacionesService"/>. Sin SQL ni reglas de negocio.</summary>
public static class NotificacionesEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/notificaciones").Auth();

        g.MapGet("/", H(async c => Api.Data(await NotificacionesService.Listar(c.Me()))));
        g.MapPut("/marcar-leidas", H(async c =>
        {
            await NotificacionesService.MarcarTodasLeidas(c.Me());
            return Api.Msg("Todas las notificaciones fueron marcadas como leídas");
        }));
        g.MapDelete("/leidas", H(async c =>
        {
            await NotificacionesService.EliminarLeidas(c.Me());
            return Api.Msg("Notificaciones leídas eliminadas");
        }));
        g.MapDelete("/{id}", H(async c =>
        {
            await NotificacionesService.Eliminar(c.Me(), Api.Route(c, "id"));
            return Api.Msg("Notificación eliminada");
        }));
        g.MapPut("/{id}/leida", H(async c => Api.Data(await NotificacionesService.MarcarLeida(c.Me(), Api.Route(c, "id")))));
    }
}
