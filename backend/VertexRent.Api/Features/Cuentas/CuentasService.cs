using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VertexRent.Api;

/// <summary>Cuentas bancarias del propietario. El SQL vive en <see cref="CuentasRepository"/>.</summary>
public static class CuentasService
{
    static readonly Regex Clabe = new(@"^\d{18}$", RegexOptions.Compiled);

    static string? Limpio(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && s.Trim().Length > 0 ? s.Trim() : null;

    public static Task<List<Row>> Listar(AuthUser u) => CuentasRepository.Listar(u.OwnerId);

    public static async Task<Row> Crear(AuthUser u, JsonObject b)
    {
        var clabe = b.S("banco_clabe");
        if (string.IsNullOrEmpty(clabe) || !Clabe.IsMatch(clabe))
            throw new AppError("La CLABE interbancaria debe tener exactamente 18 dígitos", 400);
        var esPrimera = await CuentasRepository.Contar(u.Id) == 0;   // la primera cuenta es la predeterminada
        return await CuentasRepository.Insertar(u.Id, Limpio(b["alias"]), Limpio(b["banco_nombre"]), clabe, Limpio(b["banco_titular"]), esPrimera);
    }

    /// <summary>Lo no enviado se conserva.</summary>
    public static async Task<Row> Actualizar(AuthUser u, string id, JsonObject b)
    {
        var actual = await CuentasRepository.Obtener(id, u.Id) ?? throw new AppError("Cuenta no encontrada", 404);
        if (b.ContainsKey("banco_clabe") && !(b.S("banco_clabe") is { } cl && Clabe.IsMatch(cl) && b["banco_clabe"] is JsonValue))
            throw new AppError("La CLABE interbancaria debe tener exactamente 18 dígitos", 400);
        return await CuentasRepository.Actualizar(id, u.Id,
            b.ContainsKey("alias") ? Limpio(b["alias"]) : actual.S("alias"),
            b.ContainsKey("banco_nombre") ? Limpio(b["banco_nombre"]) : actual.S("banco_nombre"),
            b["banco_clabe"] is null ? actual.S("banco_clabe") : b.S("banco_clabe"),
            b.ContainsKey("banco_titular") ? Limpio(b["banco_titular"]) : actual.S("banco_titular"),
            b.ContainsKey("activo") ? Js.T(b["activo"]) : actual.B("activo"));
    }

    public static Task<Row?> HacerPredeterminada(AuthUser u, string id) => CuentasRepository.HacerPredeterminada(id, u.Id);

    public static async Task<Dictionary<string, object?>> Eliminar(AuthUser u, string id)
    {
        var cuenta = await CuentasRepository.Obtener(id, u.Id) ?? throw new AppError("Cuenta no encontrada", 404);
        await CuentasRepository.Eliminar(id, u.Id);

        // Si era la predeterminada, promover otra cuenta activa.
        if (cuenta.B("es_predeterminada")) await CuentasRepository.PromoverPrimeraActiva(u.Id);

        // Sin ninguna cuenta: los inquilinos en transferencia/ambos pasan a efectivo.
        var pasados = await CuentasRepository.Contar(u.Id) == 0 ? await CuentasRepository.PasarInquilinosAEfectivo(u.Id) : 0;
        return new() { ["message"] = "Cuenta eliminada", ["inquilinos_a_efectivo"] = pasados };
    }

    public static async Task<Row> AsignarADepartamento(AuthUser u, string numero, JsonObject b)
    {
        var cuentaId = Js.T(b["cuenta_bancaria_id"]) ? b.S("cuenta_bancaria_id") : null;
        if (cuentaId != null && !await CuentasRepository.Existe(cuentaId, u.Id)) throw new AppError("Cuenta no encontrada", 404);
        return await CuentasRepository.AsignarADepartamento(u.Id, numero, cuentaId) ?? throw new AppError("Departamento no encontrado", 404);
    }
}
