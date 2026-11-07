namespace VertexRent.Api;

/// <summary>Rutas de cuotas extra: enlazan la petición con <see cref="CuotasService"/>. Sin SQL ni reglas de negocio.</summary>
public static class CuotasEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/cuotas").Auth().AdminOnly();
        g.MapGet("/", H(async c => Api.Data(await CuotasService.Listar(c.Me(), Api.Query(c, "inquilino_id"), Api.Query(c, "estado")))));
        g.MapPost("/", H(async c => Api.Data(await CuotasService.Crear(c.Me(), await Api.Body(c)), 201)));
        g.MapPut("/{id}/pagar", H(async c => Api.Data(await CuotasService.Pagar(c.Me(), Api.Route(c, "id")))));
        g.MapDelete("/{id}", H(async c => { await CuotasService.Eliminar(c.Me(), Api.Route(c, "id")); return Api.Ok(); }));
    }
}
