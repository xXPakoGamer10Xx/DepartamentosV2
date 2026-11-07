using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Códigos de invitación para inquilinos y colaboradores. El SQL vive en <see cref="InvitacionesRepository"/>.</summary>
public static class InvitacionesService
{
    /// <summary>Código corto "XXXX-XXXX" (sin O, 0, I, 1 para evitar confusión).</summary>
    static string GenerarCodigo()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string Parte(int n) => new(Enumerable.Range(0, n).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
        return $"{Parte(4)}-{Parte(4)}";
    }

    public static async Task<Row> Crear(AuthUser u, JsonObject b)
    {
        var rol = b["rol"] == null ? "inquilino" : b.S("rol");
        var preset = b.S("preset");
        if (rol is not ("inquilino" or "cobrador")) throw new AppError("Rol inválido. Usa \"inquilino\" o \"cobrador\"", 400);

        var permisos = new List<string>();
        string? rolLabel = null;
        if (rol == "cobrador")
        {
            var hayPreset = !string.IsNullOrEmpty(preset) && Permisos.Presets.ContainsKey(preset);
            var etiqueta = b["rol_label"] is JsonValue lv && lv.TryGetValue<string>(out var ls) ? ls.Trim() : null;
            if (hayPreset) rolLabel = Permisos.Presets[preset!].Label;
            else if (!string.IsNullOrEmpty(etiqueta)) rolLabel = etiqueta;
            else if (b["permisos"] is JsonArray) rolLabel = "Personalizado";

            if (b["permisos"] is JsonArray) permisos = Permisos.Sanitize(b["permisos"]);
            else if (hayPreset) permisos = Permisos.Presets[preset!].Permisos.ToList();
        }

        var codigo = "";
        for (var i = 0; i < 10 && codigo == ""; i++)
        {
            var candidato = GenerarCodigo();
            if (!await InvitacionesRepository.ExisteCodigo(candidato)) codigo = candidato;
        }
        if (codigo == "") throw new AppError("No se pudo generar un código único. Intenta de nuevo.", 500);

        DateTime? expiraEn = null;
        if (b["expira_dias"] != null && Js.T(b["expira_dias"]))
            expiraEn = DateTime.UtcNow.AddDays(double.Parse(Js.Str(b["expira_dias"])!, System.Globalization.CultureInfo.InvariantCulture));

        return await InvitacionesRepository.Insertar(u.Id, codigo, rol!, expiraEn, JsonX.Arr(permisos).ToJsonString(), rolLabel);
    }

    public static Task<List<Row>> Listar(AuthUser u) => InvitacionesRepository.Listar(u.Id);

    public static async Task Revocar(AuthUser u, string id)
    {
        if (!await InvitacionesRepository.Revocar(id, u.Id)) throw new AppError("Código no encontrado", 404);
    }
}
