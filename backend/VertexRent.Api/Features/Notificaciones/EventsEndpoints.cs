using System.Text.Json.Nodes;

namespace VertexRent.Api;

public static class EventsEndpoints
{
    public static void Map(WebApplication app)
    {
        // El navegador no puede enviar cabeceras en EventSource: se canjea la sesión por un boleto de 60 s para abrir el stream.
        app.MapPost("/api/events/ticket", (HttpContext c) =>
        {
            var u = c.Me();
            var ticket = Jwt.Create(new JsonObject
            {
                ["scope"] = "sse_ticket", ["id"] = u.Actor, ["email"] = u.Email, ["rol"] = u.Rol, ["nombre_completo"] = u.NombreCompleto,
            }, "60s");
            return Api.Data(new Dictionary<string, object?> { ["ticket"] = ticket });
        }).Auth();

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
