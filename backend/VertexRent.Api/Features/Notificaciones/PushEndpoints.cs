namespace VertexRent.Api;

/// <summary>Alta de suscripciones Web Push del navegador.</summary>
public static class PushEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/push");
        g.MapPost("/web-subscribe", H(async c =>
        {
            await NotificacionesService.Suscribir(c.Me(), await Api.Body(c));
            return Api.Ok();
        })).Auth();
    }
}
