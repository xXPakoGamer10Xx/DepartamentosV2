using System.Text.Json.Nodes;
using Npgsql;

namespace VertexRent.Api;

/// <summary>Reglas del módulo de departamentos. El SQL vive en <see cref="DepartamentosRepository"/>.</summary>
public static class DepartamentosService
{
    public static Task<List<Row>> Listar(AuthUser u) => DepartamentosRepository.Listar(u.Id);

    public static async Task<Row> PorNumero(AuthUser u, string numero) =>
        await DepartamentosRepository.PorNumeroConHistorial(u.Id, numero) ?? throw new AppError("Departamento no encontrado", 404);

    public static Task<Row?> Estadisticas(AuthUser u) => DepartamentosRepository.Estadisticas(u.Id);

    public static async Task<Row> Crear(AuthUser u, JsonObject b)
    {
        if (!Js.T(b["numero"])) throw new AppError("El número de departamento es requerido", 400);
        try
        {
            return await DepartamentosRepository.Insertar(u.Id, b["numero"], Js.T(b["descripcion"]) ? b.S("descripcion") : null,
                (Js.T(b["inventario_base"]) ? b["inventario_base"]! : new JsonArray()).ToJsonString());
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new AppError($"El departamento número {b.S("numero")} ya existe", 409);
        }
    }

    /// <summary>Lo no enviado se conserva.</summary>
    public static async Task<Row> Actualizar(AuthUser u, string numero, JsonObject b)
    {
        var actual = await DepartamentosRepository.Obtener(u.Id, numero) ?? throw new AppError("Departamento no encontrado", 404);
        var inventario = b.ContainsKey("inventario_base") ? b["inventario_base"]?.ToJsonString() ?? "null" : actual.J("inventario_base")?.ToJsonString();
        return (await DepartamentosRepository.Actualizar(u.Id, numero,
            b["estado"] is null ? actual.S("estado") : b.S("estado"),
            b["descripcion"] is null ? actual.S("descripcion") : b.S("descripcion"), inventario))!;
    }

    public static async Task Eliminar(AuthUser u, string numero)
    {
        if (await DepartamentosRepository.Obtener(u.Id, numero) == null) throw new AppError("Departamento no encontrado", 404);
        if (await DepartamentosRepository.TieneInquilinosActivos(u.Id, numero))
            throw new AppError("No se puede eliminar un departamento con inquilinos activos", 400);
        await DepartamentosRepository.Eliminar(u.Id, numero);
    }
}
