using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Reglas de las cuotas extra (cargos aparte de la renta). El SQL vive en <see cref="CuotasRepository"/>.</summary>
public static class CuotasService
{
    public static Task<List<Row>> Listar(AuthUser u, string? inquilinoId, string? estado) => CuotasRepository.Listar(u.Id, inquilinoId, estado);

    public static async Task<Row> Crear(AuthUser u, JsonObject b)
    {
        var concepto = b.S("concepto")?.Trim();
        if (!Js.T(b["inquilino_id"]) || string.IsNullOrEmpty(concepto) || !Js.T(b["monto"]))
            throw new AppError("inquilino_id, concepto y monto son requeridos", 400);
        var monto = Js.ParseFloat(b["monto"]);
        if (double.IsNaN(monto) || monto <= 0) throw new AppError("El monto debe ser mayor a cero", 400);

        var inquilino = await CuotasRepository.InquilinoDelPropietario(b.S("inquilino_id"), u.Id)
            ?? throw new AppError("Inquilino no encontrado o no autorizado", 403);
        var cuota = await CuotasRepository.Insertar(b.S("inquilino_id"), concepto, (decimal)monto, Js.CurrentPeriodo(), u.Actor);

        var usuarioId = inquilino.S("usuario_id");
        if (!string.IsNullOrEmpty(usuarioId))
            await PushService.CreateAndSendNotification(usuarioId, "🔔 Nuevo cargo adicional",
                $"Se ha generado un cobro extra de {Js.MXN((decimal)monto)} por: {concepto}", "cuota");
        return cuota;
    }

    public static async Task Eliminar(AuthUser u, string id)
    {
        if (!await CuotasRepository.EliminarPendiente(id, u.Id)) throw new AppError("Cuota no encontrada o ya fue pagada", 404);
    }

    /// <summary>La cuota se liquida por separado: no genera abonos sobre la renta del mes.</summary>
    public static async Task<Dictionary<string, object?>> Pagar(AuthUser u, string id)
    {
        if (!await CuotasRepository.MarcarPagada(id, u.Id)) throw new AppError("Cargo no encontrado o ya fue pagado", 404);
        return new() { ["cuota_id"] = id, ["estado"] = "pagado" };
    }
}
