namespace VertexRent.Api;

/// <summary>Rutas de tickets: enlazan la petición con <see cref="TicketsService"/>. Sin SQL ni reglas de negocio.</summary>
public static class TicketsEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/tickets").Auth();

        // El inquilino siempre puede ver/crear/borrar sus propios tickets;
        // admin/cobrador necesitan el permiso 'tickets'.
        g.MapGet("/", H(async c => Api.Data(await TicketsService.Listar(c.Me(), Api.Query(c, "estado"), Api.Query(c, "mes"))))).InquilinoOrPermiso("tickets");
        g.MapPost("/", H(async c => Api.Data(await TicketsService.Crear(c.Me(), await Api.Body(c)), 201)));
        g.MapPatch("/{id}", H(async c => Api.Data(await TicketsService.Actualizar(c.Me(), Api.Route(c, "id"), await Api.Body(c))))).Permiso("tickets");
        g.MapDelete("/{id}", H(async c => { await TicketsService.Eliminar(c.Me(), Api.Route(c, "id")); return Api.Ok(); })).InquilinoOrPermiso("tickets");
    }
}
