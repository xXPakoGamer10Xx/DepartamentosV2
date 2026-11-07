namespace VertexRent.Api;

/// <summary>Rutas de departamentos: enlazan la petición con <see cref="DepartamentosService"/>. Sin SQL ni reglas de negocio.</summary>
public static class DepartamentosEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/departamentos").Auth();

        g.MapGet("/stats", H(async c => Api.Data(await DepartamentosService.Estadisticas(c.Me())))).Permiso("departamentos");
        g.MapGet("/", H(async c => Api.Data(await DepartamentosService.Listar(c.Me())))).Permiso("departamentos");
        g.MapGet("/{numero}", H(async c => Api.Data(await DepartamentosService.PorNumero(c.Me(), Api.Route(c, "numero"))))).Permiso("departamentos");
        g.MapPost("/", H(async c => Api.Data(await DepartamentosService.Crear(c.Me(), await Api.Body(c)), 201))).AdminOnly();
        g.MapPut("/{numero}", H(async c => Api.Data(await DepartamentosService.Actualizar(c.Me(), Api.Route(c, "numero"), await Api.Body(c))))).AdminOnly();
        g.MapDelete("/{numero}", H(async c =>
        {
            await DepartamentosService.Eliminar(c.Me(), Api.Route(c, "numero"));
            return Api.Msg("Departamento eliminado correctamente");
        })).AdminOnly();
    }
}
