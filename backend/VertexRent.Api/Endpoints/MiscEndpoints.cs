using System.Text.Json.Nodes;

namespace VertexRent.Api;

public static class NotificacionesEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/notificaciones").Auth();

        g.MapGet("/", async (HttpContext c) =>
        {
            var r = await Db.Query(
                @"SELECT * FROM notificaciones
                  WHERE usuario_id = $1
                  ORDER BY created_at DESC
                  LIMIT 50", c.Me().Id);
            return Api.Data(r.Rows);
        });

        g.MapPut("/marcar-leidas", async (HttpContext c) =>
        {
            await Db.Query("UPDATE notificaciones SET leido = true WHERE usuario_id = $1", c.Me().Id);
            return Api.Msg("Todas las notificaciones fueron marcadas como leídas");
        });

        g.MapPut("/{id}/leida", async (HttpContext c) =>
        {
            var id = Api.Route(c, "id");
            var uid = c.Me().Id;
            var check = await Db.Query("SELECT id FROM notificaciones WHERE id = $1 AND usuario_id = $2", id, uid);
            if (check.First == null) throw new AppError("Notificación no encontrada o no pertenece al usuario", 404);
            var r = await Db.Query("UPDATE notificaciones SET leido = true WHERE id = $1 AND usuario_id = $2 RETURNING *", id, uid);
            return Api.Data(r.First);
        });
    }
}

public static class PushEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/push");

        g.MapPost("/token", async (HttpContext c) =>
        {
            var b = await Api.Body(c);
            if (!(b["token"] is JsonValue v && v.TryGetValue<string>(out var token) && token.Length > 0))
                return Api.Fail("Token requerido", 400);
            await Db.Query(
                @"INSERT INTO push_tokens (usuario_id, token) VALUES ($1, $2)
                  ON CONFLICT (usuario_id, token) DO NOTHING", c.Me().Id, token);
            return Api.Ok();
        }).Auth();

        g.MapPost("/web-subscribe", async (HttpContext c) =>
        {
            var b = await Api.Body(c);
            var endpoint = b.S("endpoint");
            var keys = b["keys"] as JsonObject;
            if (!Js.T(b["endpoint"]) || !Js.T(keys?["p256dh"]) || !Js.T(keys?["auth"]))
                return Api.Fail("Suscripción inválida", 400);
            await Db.Query(
                @"INSERT INTO web_push_subscriptions (usuario_id, endpoint, p256dh, auth)
                  VALUES ($1, $2, $3, $4)
                  ON CONFLICT (endpoint) DO UPDATE SET usuario_id = $1, p256dh = $3, auth = $4",
                c.Me().Id, endpoint, keys!.S("p256dh"), keys!.S("auth"));
            return Api.Ok();
        }).Auth();
    }
}

public static class EventsEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/events", async (HttpContext c) =>
        {
            var u = c.Me();
            c.Response.Headers.ContentType = "text/event-stream";
            c.Response.Headers.CacheControl = "no-cache, no-transform";
            c.Response.Headers["X-Accel-Buffering"] = "no";
            await c.Response.Body.FlushAsync();

            var client = new SseClient { Res = c.Response };
            SseService.Add(u.Id, u.Rol, client);
            try
            {
                await client.Write("event: connected\ndata: {}\n\n");
                while (!c.RequestAborted.IsCancellationRequested)
                {
                    await Task.Delay(25000, c.RequestAborted);
                    if (!await client.Write(":ping\n\n")) break;
                }
            }
            catch (OperationCanceledException) { }
            finally { SseService.Remove(u.Id, client); }
            return Results.Empty;
        }).SseAuth();
    }
}

public static class BackupsEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/backups").Auth().AdminOnly();

        g.MapGet("/", async () =>
            Api.Data((await Db.Query("SELECT * FROM backups ORDER BY created_at DESC LIMIT 50")).Rows));

        g.MapPost("/manual", async () =>
        {
            var (archivo, tamano) = await BackupService.RunBackup();
            await Db.Query("UPDATE backups SET tipo = 'manual' WHERE archivo = $1", archivo);
            return Api.Json(new Dictionary<string, object?>
            {
                ["success"] = true, ["message"] = "Backup manual completado",
                ["data"] = new Dictionary<string, object?> { ["archivo"] = archivo, ["tamano_bytes"] = tamano },
            });
        });

        g.MapGet("/{archivo}/descargar", (HttpContext c) =>
        {
            // Sanitizar nombre para evitar path traversal
            var safe = Path.GetFileName(Api.Route(c, "archivo"));
            var path = Path.Combine(Env.Get("BACKUP_DIR", "./backups"), safe);
            if (!File.Exists(path)) throw new AppError("Archivo de backup no encontrado", 404);
            return Results.File(Path.GetFullPath(path), "application/sql", safe);
        });
    }
}
