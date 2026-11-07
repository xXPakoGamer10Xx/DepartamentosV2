namespace VertexRent.Api;

/// <summary>Rutas de inquilinos: enlazan la petición con <see cref="InquilinosService"/>. Sin SQL ni reglas de negocio.</summary>
public static class InquilinosEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/inquilinos");

        // El PDF lo abre el navegador directamente (sin headers): token de un solo propósito (ver PdfAuth).
        g.MapGet("/{id}/pdf", H(async c =>
        {
            var (pdf, nombre) = await InquilinosService.PdfDelContrato(c.Me().Id, Api.Route(c, "id"));
            return Descargas.Pdf(c, pdf, nombre);
        })).PdfAuth();

        var a = g.MapGroup("").Auth();

        a.MapPost("/{id}/pdf-token", H(async c =>
            Api.Data(new Dictionary<string, object?> { ["token"] = await InquilinosService.GenerarTokenPdf(c.Me(), Api.Route(c, "id")) }))).Permiso("contratos");

        a.MapGet("/mi-depto", H(async c => Api.Data(await InquilinosService.MiDepto(c.Me()))));
        a.MapPost("/extraer-ine", H(async c => Api.Data(await IneService.Extraer((await Api.Body(c)).S("imagen_base64"))))).AdminOnly();

        a.MapGet("/", H(Listar)).Permiso("inquilinos");
        a.MapGet("/{id}", H(async c => Api.Data(await InquilinosService.PorId(c.Me(), Api.Route(c, "id"))))).Permiso("inquilinos");
        a.MapPost("/", H(async c => Api.Data(await InquilinosService.Crear(c.Me(), Api.Ip(c), await Api.Body(c)), 201))).Permiso("inquilinos.editar");
        a.MapPut("/{id}", H(async c => Api.Data(await InquilinosService.Actualizar(c.Me(), Api.Ip(c), Api.Route(c, "id"), await Api.Body(c))))).Permiso("inquilinos.editar");
        a.MapPut("/{id}/vincular-usuario", H(async c =>
            Api.Data(await InquilinosService.Vincular(c.Me(), Api.Route(c, "id"), (await Api.Body(c))["usuario_id"])))).AdminOnly();
        a.MapDelete("/{id}", H(async c =>
        {
            await InquilinosService.DarDeBaja(c.Me(), Api.Ip(c), Api.Route(c, "id"));
            return Api.Msg("Inquilino dado de baja correctamente");
        })).Permiso("inquilinos.editar");
    }

    static async Task<IResult> Listar(HttpContext c)
    {
        var r = await InquilinosService.Listar(c.Me(), Api.Query(c, "search"), Api.Query(c, "estado"), Api.Query(c, "depto"));
        return Api.Obj(200, ("data", r.Rows), ("total", r.RowCount));
    }
}
