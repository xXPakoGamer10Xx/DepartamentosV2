using System.Text.Json.Nodes;

namespace VertexRent.Api;

public static class PushEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/push");

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
                c.Me().Actor, endpoint, keys!.S("p256dh"), keys!.S("auth"));
            return Api.Ok();
        }).Auth();
    }
}
