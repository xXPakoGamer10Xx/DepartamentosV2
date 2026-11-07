namespace VertexRent.Api;

/// <summary>Rutas de configuración: enlazan la petición con <see cref="ConfigService"/>. Sin SQL ni reglas de negocio.</summary>
public static class ConfigEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/config").Auth();

        g.MapGet("/", H(async c => Api.Data(await ConfigService.Obtener(c.Me(), Api.Query(c, "depto_numero")))));
        g.MapPut("/", H(async c => { await ConfigService.Actualizar(c.Me(), await Api.Body(c)); return Api.Msg("Configuración actualizada"); })).AdminOnly();
    }
}
