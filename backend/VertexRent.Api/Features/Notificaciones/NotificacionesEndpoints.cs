using System.Text.Json.Nodes;

namespace VertexRent.Api;

public static class NotificacionesEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/notificaciones").Auth();

        g.MapGet("/", async (HttpContext c) =>
        {
            // Las ya leídas se limpian solas pasados 7 días.
            await Db.Query("DELETE FROM notificaciones WHERE usuario_id = $1 AND leido = true AND created_at < now() - interval '7 days'", c.Me().Actor);
            var r = await Db.Query(
                @"SELECT * FROM notificaciones
                  WHERE usuario_id = $1
                  ORDER BY created_at DESC
                  LIMIT 50", c.Me().Actor);
            return Api.Data(r.Rows);
        });

        g.MapPut("/marcar-leidas", async (HttpContext c) =>
        {
            await Db.Query("UPDATE notificaciones SET leido = true WHERE usuario_id = $1", c.Me().Actor);
            return Api.Msg("Todas las notificaciones fueron marcadas como leídas");
        });

        g.MapDelete("/leidas", async (HttpContext c) =>
        {
            await Db.Query("DELETE FROM notificaciones WHERE usuario_id = $1 AND leido = true", c.Me().Actor);
            return Api.Msg("Notificaciones leídas eliminadas");
        });

        g.MapDelete("/{id}", async (HttpContext c) =>
        {
            await Db.Query("DELETE FROM notificaciones WHERE id = $1 AND usuario_id = $2", Api.Route(c, "id"), c.Me().Actor);
            return Api.Msg("Notificación eliminada");
        });

        g.MapPut("/{id}/leida", async (HttpContext c) =>
        {
            var id = Api.Route(c, "id");
            var uid = c.Me().Actor;
            var check = await Db.Query("SELECT id FROM notificaciones WHERE id = $1 AND usuario_id = $2", id, uid);
            if (check.First == null) throw new AppError("Notificación no encontrada o no pertenece al usuario", 404);
            var r = await Db.Query("UPDATE notificaciones SET leido = true WHERE id = $1 AND usuario_id = $2 RETURNING *", id, uid);
            return Api.Data(r.First);
        });
    }
}
