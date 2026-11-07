namespace VertexRent.Api;

/// <summary>Rutas de reportes: enlazan la petición con <see cref="ReportesService"/>. Sin SQL ni reglas de negocio.</summary>
public static class ReportesEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/reportes").Auth().Permiso("reportes");
        g.MapGet("/anual", H(async c => Api.Data(await ReportesService.Anual(c.Me(), ReportesService.ValidarAnio(Api.Query(c, "year"))))));
        g.MapGet("/mensual", H(async c => Api.Data(await ReportesService.Mensual(c.Me(), ReportesService.ValidarAnio(Api.Query(c, "year"))))));
    }
}
