using System.Text.Json.Nodes;

namespace VertexRent.Api;

public sealed record PlantillaCargada(string Id, string Nombre, byte[] Docx, JsonArray Campos, bool Sistema);

public sealed record ContratoContexto(Row Inquilino, Row? Depto, Dictionary<string, string> Config, Dictionary<string, object?> Data);

/// <summary>Carga de datos y resolución de plantilla para generar un contrato.</summary>
public static class ContratoServicio
{
    static readonly string[] ClavesConfig = { "arrendador_nombre", "arrendador_direccion", "arrendador_rfc", "arrendador_telefono" };

    /// <summary>Inquilino + propiedad + arrendador + cuenta bancaria, ya mezclados en el diccionario de datos.</summary>
    public static async Task<ContratoContexto> CargarContexto(string adminId, string inquilinoId)
    {
        var r = await Db.Query("SELECT * FROM inquilinos WHERE admin_id = $1 AND id = $2", adminId, inquilinoId);
        var inq = r.First ?? throw new AppError("Inquilino no encontrado", 404);

        var cfgTask = Db.Query("SELECT clave, valor FROM configuracion WHERE admin_id = $1 AND clave = ANY($2)", adminId, ClavesConfig);
        var deptoTask = Db.Query(
            @"SELECT id, numero, descripcion, ubicacion, inventario_base, cuenta_bancaria_id, plantilla_id
              FROM departamentos WHERE admin_id = $1 AND numero = $2", adminId, inq.S("depto_numero"));
        var cuentaTask = Db.Query(
            @"SELECT cb.banco_nombre, cb.banco_clabe, cb.banco_titular
              FROM cuentas_bancarias cb
              WHERE cb.admin_id = $1 AND cb.activo = TRUE
                AND (cb.id = (SELECT d.cuenta_bancaria_id FROM departamentos d WHERE d.admin_id = $1 AND d.numero = $2)
                     OR cb.es_predeterminada = TRUE)
              ORDER BY (cb.id = (SELECT d.cuenta_bancaria_id FROM departamentos d WHERE d.admin_id = $1 AND d.numero = $2)) DESC
              LIMIT 1", adminId, inq.S("depto_numero"));
        await Task.WhenAll(cfgTask, deptoTask, cuentaTask);

        var config = cfgTask.Result.Rows.ToDictionary(x => x.S("clave")!, x => x.S("valor") ?? "");
        var depto = deptoTask.Result.First;
        var data = new Dictionary<string, object?>(inq);
        foreach (var k in ClavesConfig) data[k] = config.GetValueOrDefault(k) ?? "";
        data["inventario_base"] = depto?.J("inventario_base") ?? new JsonArray();
        data["propiedad_direccion"] = depto?.S("ubicacion") ?? "";
        data["propiedad_descripcion"] = depto?.S("descripcion") ?? "";
        if (cuentaTask.Result.First is { } cu)
        {
            data["banco_nombre"] = cu.S("banco_nombre") ?? "";
            data["banco_clabe"] = cu.S("banco_clabe") ?? "";
            data["banco_titular"] = cu.S("banco_titular") ?? "";
        }
        return new ContratoContexto(inq, depto, config, data);
    }

    /// <summary>Huella del texto de una plantilla (ignora formato y espacios): detecta cambios reales de contenido.</summary>
    public static string HashTexto(byte[] docx)
    {
        var texto = string.Join("\n", DocxService.ExtraerParrafos(docx).Select(p => System.Text.RegularExpressions.Regex.Replace(p.Texto, @"\s+", " ").Trim()));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();
    }

    public static PlantillaCargada Sistema() =>
        new(PlantillaSistema.Id, PlantillaSistema.Nombre, PlantillaSistema.Docx, new JsonArray(), true);

    public static async Task<PlantillaCargada> CargarPlantilla(string adminId, string id)
    {
        if (id == PlantillaSistema.Id) return Sistema();
        if (!Guid.TryParse(id, out _)) throw new AppError("Plantilla no encontrada", 404);
        var r = await Db.Query(
            "SELECT id, nombre, docx_plantilla, campos FROM plantillas_contrato WHERE admin_id = $1 AND id = $2", adminId, id);
        return Mapear(r.First ?? throw new AppError("Plantilla no encontrada", 404));
    }

    static PlantillaCargada Mapear(Row p) =>
        new(p.S("id")!, p.S("nombre")!, (byte[])p["docx_plantilla"]!, p.J("campos") as JsonArray ?? new JsonArray(), false);

    /// <summary>Plantilla que corresponde a un inquilino: la suya → la de su propiedad → la predeterminada → la del sistema.</summary>
    public static async Task<PlantillaCargada> ResolverPlantilla(string adminId, Row inquilino, Row? depto)
    {
        var r = await Db.Query(
            @"SELECT id, nombre, docx_plantilla, campos FROM plantillas_contrato
              WHERE admin_id = $1 AND estado = 'lista' AND (id = $2 OR id = $3 OR predeterminada)
              ORDER BY (id = $2) DESC NULLS LAST, (id = $3) DESC NULLS LAST, predeterminada DESC
              LIMIT 1",
            adminId, Guid.TryParse(inquilino.S("plantilla_id"), out var g1) ? g1 : (object?)null,
            Guid.TryParse(depto?.S("plantilla_id"), out var g2) ? g2 : (object?)null);
        return r.First != null ? Mapear(r.First) : Sistema();
    }

    /// <summary>Valores de los campos personalizados, listos para rellenar la plantilla.</summary>
    public static Dictionary<string, string> ValoresExtra(JsonArray campos, JsonObject? entrada)
    {
        var res = new Dictionary<string, string>();
        foreach (var c in campos)
        {
            var key = Js.Str(c?["key"]); if (string.IsNullOrEmpty(key)) continue;
            var valor = Js.Str(entrada?[key])?.Trim() ?? "";
            if (valor.Length == 0 && c!["requerido"]?.GetValue<bool>() == true)
                throw new AppError($"Falta el dato \"{Js.Str(c["etiqueta"]) ?? key}\"", 400);
            if (Js.Str(c!["tipo"]) == "fecha" && valor.Length > 0) valor = ContractFormat.LongDate(valor, true);
            res[key] = valor;
        }
        return res;
    }

    public static Task<ContratoResultado> Generar(PlantillaCargada p, ContratoContexto ctx, JsonObject? extra) =>
        ContratoRenderer.Generar(p.Docx, ctx.Data, ValoresExtra(p.Campos, extra));
}
