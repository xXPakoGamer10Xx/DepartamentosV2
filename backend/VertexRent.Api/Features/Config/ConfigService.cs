using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VertexRent.Api;

/// <summary>Configuración del propietario (datos del arrendador, recordatorios, gracia). El SQL vive en <see cref="ConfigRepository"/>.</summary>
public static class ConfigService
{
    static readonly string[] Permitidas =
    {
        "arrendador_nombre", "arrendador_direccion", "arrendador_rfc", "arrendador_telefono",
        "banco_nombre", "banco_clabe", "banco_titular",
        "admin_invite_code", "app_url",
        "dias_gracia_retraso",
        "usa_qr_inquilinos", "recordatorios_renta_dias",
    };

    // Lo único que ve un inquilino: datos para pagar y contactar (nunca códigos de invitación ni RFC).
    static readonly HashSet<string> VisiblesInquilino = new()
    {
        "arrendador_nombre", "arrendador_direccion", "arrendador_telefono",
        "banco_nombre", "banco_clabe", "banco_titular", "app_url", "dias_gracia_retraso", "usa_qr_inquilinos",
    };

    /// <param name="deptoNumero">El admin puede pedir la cuenta bancaria resuelta para un depto (pantalla "Cobrar Renta").</param>
    public static async Task<Dictionary<string, object?>> Obtener(AuthUser u, string? deptoNumero)
    {
        var adminId = u.OwnerId;
        if (u.Rol == "inquilino")
        {
            var inq = await ConfigRepository.InquilinoActivoDelUsuario(u.Id);
            if (inq == null) return new();
            adminId = inq.S("admin_id")!;
            deptoNumero = inq.S("depto_numero");
        }

        var config = new Dictionary<string, object?>();
        foreach (var fila in await ConfigRepository.Claves(adminId))
            if (u.Rol != "inquilino" || VisiblesInquilino.Contains(fila.S("clave")!))
                config[fila.S("clave")!] = fila.S("valor");

        if (deptoNumero != null && await ConfigRepository.CuentaDelDepartamento(adminId, deptoNumero) is { } cuenta)
        {
            config["banco_nombre"] = cuenta.S("banco_nombre") ?? "";
            config["banco_clabe"] = cuenta.S("banco_clabe") ?? "";
            config["banco_titular"] = cuenta.S("banco_titular") ?? "";
        }
        return config;
    }

    public static async Task Actualizar(AuthUser u, JsonObject cambios)
    {
        if (u.Rol != "admin") throw new AppError("No autorizado", 403);
        foreach (var (clave, nodo) in cambios.ToList())
        {
            if (!Permitidas.Contains(clave)) continue;
            var valor = Js.Str(nodo) ?? "null";

            if (clave == "dias_gracia_retraso" && Js.ParseInt(valor) is null or < 0 or > 90)
                throw new AppError("Los días de gracia deben ser un número entre 0 y 90", 400);
            if (clave == "recordatorios_renta_dias" && valor.Trim() != "")
            {
                var partes = valor.Trim().Split(',').Select(s => s.Trim()).ToArray();
                if (partes.Length > 6 || !partes.All(s => Regex.IsMatch(s, @"^\d{1,2}$") && int.Parse(s) <= 30))
                    throw new AppError("Recordatorios inválidos: hasta 6 valores entre 0 y 30 días, separados por coma", 400);
                // Normaliza la lista: enteros únicos, orden descendente.
                valor = string.Join(",", partes.Select(s => int.Parse(s)).Distinct().OrderByDescending(n => n));
            }
            await ConfigRepository.Guardar(u.Id, clave, valor);
        }
    }
}
