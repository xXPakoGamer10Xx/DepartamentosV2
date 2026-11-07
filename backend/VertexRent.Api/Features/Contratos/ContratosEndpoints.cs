namespace VertexRent.Api;

/// <summary>Rutas de contratos y plantillas: enlazan la petición con <see cref="ContratosService"/>. Sin SQL ni reglas de negocio.</summary>
public static class ContratosEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/contratos").Auth();

        g.MapGet("/variables", H(c => Task.FromResult(Api.Data(ContratosService.Variables())))).Permiso("contratos");
        g.MapGet("/plantillas", H(async c => Api.Data(await ContratosService.ListarPlantillas(c.Me())))).Permiso("contratos");
        g.MapPost("/plantillas", H(async c => Api.Data(await ContratosService.SubirPlantilla(c.Me(), Api.Ip(c), await Api.Body(c)), 201))).AdminOnly();
        g.MapGet("/plantillas/{id}", H(async c => Api.Data(await ContratosService.DetallePlantilla(c.Me(), Api.Route(c, "id"))))).Permiso("contratos");
        g.MapPut("/plantillas/{id}", H(async c => Api.Data(await ContratosService.GuardarPlantilla(c.Me(), Api.Ip(c), Api.Route(c, "id"), await Api.Body(c))))).AdminOnly();
        g.MapDelete("/plantillas/{id}", H(async c =>
        {
            await ContratosService.EliminarPlantilla(c.Me(), Api.Ip(c), Api.Route(c, "id"));
            return Api.Msg("Plantilla eliminada");
        })).AdminOnly();
        g.MapPost("/plantillas/{id}/preview", H(async c =>
        {
            var (pdf, aproximado) = await ContratosService.VistaPrevia(c.Me(), Api.Route(c, "id"), await Api.Body(c));
            c.Response.Headers["X-Aproximado"] = aproximado ? "1" : "0";
            return Descargas.Pdf(c, pdf, "vista_previa.pdf");
        })).Permiso("contratos");
        g.MapGet("/plantillas/{id}/docx", H(async c =>
        {
            var (docx, nombre) = await ContratosService.DescargarPlantilla(c.Me(), Api.Route(c, "id"));
            return Descargas.Docx(c, docx, nombre);
        })).Permiso("contratos");
        g.MapPost("/plantillas/{id}/predeterminada", H(async c =>
        {
            await ContratosService.HacerPredeterminada(c.Me(), Api.Route(c, "id"));
            return Api.Msg("Plantilla predeterminada actualizada");
        })).AdminOnly();
        g.MapPut("/plantillas/{id}/asignar", H(async c =>
        {
            await ContratosService.Asignar(c.Me(), Api.Route(c, "id"), await Api.Body(c));
            return Api.Msg("Plantilla asignada");
        })).AdminOnly();

        g.MapGet("/inquilino/{id}/preparar", H(async c => Api.Data(await ContratosService.Preparar(c.Me(), Api.Route(c, "id"), Api.Query(c, "plantilla_id"))))).Permiso("contratos");
        g.MapPost("/inquilino/{id}/generar", H(async c => Api.Data(await ContratosService.Generar(c.Me(), Api.Ip(c), Api.Route(c, "id"), await Api.Body(c)), 201))).Permiso("contratos");
        g.MapPost("/generar-pendientes", H(async c => Api.Data(await ContratosService.GenerarPendientes(c.Me(), Api.Ip(c))))).Permiso("contratos");

        g.MapGet("/resumen", H(async c => Api.Data(await ContratosService.Resumen(c.Me())))).Permiso("contratos");
        g.MapGet("/", H(async c => Api.Data(await ContratosService.Historial(c.Me(), Api.Query(c, "inquilino_id"))))).Permiso("contratos");
        g.MapGet("/{id}/archivo", H(async c =>
        {
            var (bytes, nombre, esDocx) = await ContratosService.Archivo(c.Me(), Api.Route(c, "id"), Api.Query(c, "formato") == "docx" ? "docx" : "pdf");
            return esDocx ? Descargas.Docx(c, bytes, nombre) : Descargas.Pdf(c, bytes, nombre);
        })).Permiso("contratos");
        g.MapDelete("/{id}", H(async c =>
        {
            await ContratosService.Eliminar(c.Me(), Api.Ip(c), Api.Route(c, "id"));
            return Api.Msg("Contrato eliminado");
        })).Permiso("contratos");
    }
}
