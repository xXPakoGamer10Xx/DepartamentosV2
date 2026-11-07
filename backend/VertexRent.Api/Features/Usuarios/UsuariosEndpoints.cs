namespace VertexRent.Api;

/// <summary>Rutas de usuarios: enlazan la petición con <see cref="UsuariosService"/>. Sin SQL ni reglas de negocio.</summary>
public static class UsuariosEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/usuarios").Auth();

        g.MapGet("/", H(async c => Api.Data(await UsuariosService.Listar(c.Me())))).AdminOnly();
        g.MapPost("/", H(Crear)).AdminOnly();
        g.MapPut("/{id}/toggle", H(async c => Api.Data(await UsuariosService.AlternarActivo(c.Me(), Api.Route(c, "id"))))).AdminOnly();
        g.MapPatch("/perfil", H(async c => Api.Data(await UsuariosService.ActualizarPerfil(c.Me(), await Api.Body(c)))));
        g.MapPatch("/{id}/rol", H(async c => Api.Data(await UsuariosService.CambiarRol(c.Me(), Api.Route(c, "id"), (await Api.Body(c)).S("rol"))))).AdminOnly();
        g.MapPatch("/{id}/permisos", H(async c => Api.Data(await UsuariosService.ActualizarPermisos(c.Me(), Api.Route(c, "id"), await Api.Body(c))))).AdminOnly();
        g.MapDelete("/{id}", H(async c =>
        {
            await UsuariosService.Eliminar(c.Me(), Api.Route(c, "id"));
            return Api.Msg("Usuario eliminado exitosamente");
        })).AdminOnly();
    }

    static async Task<IResult> Crear(HttpContext c)
    {
        var b = await Api.Body(c);
        if (new Validator(b).Email("email").Result is { } bad) return bad;   // normaliza el correo igual que el registro y el login
        return Api.Data(await UsuariosService.Crear(c.Me(), b), 201);
    }
}
