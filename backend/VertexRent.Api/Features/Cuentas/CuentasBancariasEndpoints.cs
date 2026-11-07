namespace VertexRent.Api;

/// <summary>
/// Rutas de cuentas bancarias. La forma del cuerpo se valida aquí (<see cref="Validator"/>); las reglas viven en
/// <see cref="CuentasService"/>.
/// </summary>
public static class CuentasBancariasEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/cuentas-bancarias").Auth();

        // Ver: admin o colaborador con permiso 'cuentas'. Crear/editar/borrar: solo admin.
        g.MapGet("/", H(async c => Api.Data(await CuentasService.Listar(c.Me())))).Permiso("cuentas");
        g.MapPost("/", H(Crear)).AdminOnly();
        g.MapPut("/{id}", H(async c => Api.Data(await CuentasService.Actualizar(c.Me(), Api.Route(c, "id"), await Api.Body(c))))).AdminOnly();
        g.MapPatch("/{id}/predeterminada", H(async c => Api.Data(await CuentasService.HacerPredeterminada(c.Me(), Api.Route(c, "id"))))).AdminOnly();
        g.MapDelete("/{id}", H(async c =>
        {
            var r = await CuentasService.Eliminar(c.Me(), Api.Route(c, "id"));
            return Api.Obj(200, r.Select(kv => (kv.Key, kv.Value)).ToArray());
        })).AdminOnly();
        g.MapPatch("/departamento/{numero}", H(async c => Api.Data(await CuentasService.AsignarADepartamento(c.Me(), Api.Route(c, "numero"), await Api.Body(c))))).AdminOnly();
    }

    static async Task<IResult> Crear(HttpContext c)
    {
        var b = await Api.Body(c);
        var v = new Validator(b)
            .Match("banco_clabe", @"^\d{18}$", "La CLABE debe tener 18 dígitos")
            .Str("banco_nombre", max: 60, optional: true, trim: true)
            .Str("banco_titular", max: 120, optional: true, trim: true)
            .Str("alias", max: 60, optional: true, trim: true);
        if (v.Result is { } bad) return bad;
        return Api.Data(await CuentasService.Crear(c.Me(), b), 201);
    }
}
