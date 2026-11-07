namespace VertexRent.Api;

/// <summary>Rutas de depósitos: enlazan la petición con <see cref="DepositosService"/>. Sin SQL ni reglas de negocio.</summary>
public static class DepositosEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/depositos").Auth();

        g.MapPost("/abono", H(async c => Api.Data(await DepositosService.RegistrarAbono(c.Me(), await Api.Body(c))))).Permiso("pagos.marcar");
        g.MapPut("/abono/{abono_id}", H(async c => Api.Data(await DepositosService.EditarAbono(c.Me(), Api.Route(c, "abono_id"), await Api.Body(c))))).AdminOnly();
        g.MapDelete("/abono/{abono_id}", H(async c => { await DepositosService.EliminarAbono(c.Me(), Api.Route(c, "abono_id")); return Api.Ok(); })).AdminOnly();
        g.MapGet("/{inquilino_id}/saldo", H(async c => Api.Data(await DepositosService.Saldo(c.Me(), Api.Route(c, "inquilino_id"))))).InquilinoOrPermiso("pagos");
        g.MapGet("/{inquilino_id}/abonos", H(async c => Api.Data(await DepositosService.Abonos(c.Me(), Api.Route(c, "inquilino_id"))))).InquilinoOrPermiso("pagos");
    }
}
