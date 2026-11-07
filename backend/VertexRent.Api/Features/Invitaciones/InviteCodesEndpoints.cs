namespace VertexRent.Api;

/// <summary>Rutas de invitaciones: enlazan la petición con <see cref="InvitacionesService"/>. Sin SQL ni reglas de negocio.</summary>
public static class InviteCodesEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/invite-codes").Auth().AdminOnly();
        g.MapPost("/", H(async c => Api.Data(await InvitacionesService.Crear(c.Me(), await Api.Body(c)), 201)));
        g.MapGet("/", H(async c => Api.Data(await InvitacionesService.Listar(c.Me()))));
        g.MapDelete("/{id}", H(async c => { await InvitacionesService.Revocar(c.Me(), Api.Route(c, "id")); return Api.Msg("Código revocado"); }));
    }
}
