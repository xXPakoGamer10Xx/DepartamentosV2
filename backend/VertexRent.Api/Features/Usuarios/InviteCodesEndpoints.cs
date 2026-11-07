using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace VertexRent.Api;

public static class InviteCodesEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/invite-codes").Auth().AdminOnly();
        g.MapPost("/", H(Crear));
        g.MapGet("/", H(Listar));
        g.MapDelete("/{id}", H(Revocar));
    }

    // Código corto "XXXX-XXXX" (sin O, 0, I, 1 para evitar confusión)
    static string GenerarCodigo()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string Parte(int n) => new(Enumerable.Range(0, n).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
        return $"{Parte(4)}-{Parte(4)}";
    }

    static async Task<IResult> Crear(HttpContext c)
    {
        var b = await Api.Body(c);
        var rol = b["rol"] == null ? "inquilino" : b.S("rol");
        var preset = b.S("preset");

        if (rol is not ("inquilino" or "cobrador"))
            throw new AppError("Rol inválido. Usa \"inquilino\" o \"cobrador\"", 400);

        var permisos = new List<string>();
        string? rolLabel = null;
        if (rol == "cobrador")
        {
            var hasPreset = !string.IsNullOrEmpty(preset) && Permisos.Presets.ContainsKey(preset);
            var labelBody = b["rol_label"] is JsonValue lv && lv.TryGetValue<string>(out var ls) ? ls.Trim() : null;
            if (hasPreset) rolLabel = Permisos.Presets[preset!].Label;
            else if (!string.IsNullOrEmpty(labelBody)) rolLabel = labelBody;
            else if (b["permisos"] is JsonArray) rolLabel = "Personalizado";

            if (b["permisos"] is JsonArray) permisos = Permisos.Sanitize(b["permisos"]);
            else if (hasPreset) permisos = Permisos.Presets[preset!].Permisos.ToList();
        }

        var codigo = "";
        for (var i = 0; i < 10; i++)
        {
            var cand = GenerarCodigo();
            var ex = await Db.Query("SELECT 1 FROM codigos_invitacion WHERE codigo = $1", cand);
            if (ex.Rows.Count == 0) { codigo = cand; break; }
        }
        if (codigo == "") throw new AppError("No se pudo generar un código único. Intenta de nuevo.", 500);

        DateTime? expiraEn = null;
        if (b["expira_dias"] != null && Js.T(b["expira_dias"]))
        {
            var dias = double.Parse(Js.Str(b["expira_dias"])!, System.Globalization.CultureInfo.InvariantCulture);
            expiraEn = DateTime.UtcNow.AddDays(dias);
        }

        var r = await Db.Query(
            @"INSERT INTO codigos_invitacion (admin_id, codigo, rol, expira_en, permisos, rol_label)
              VALUES ($1, $2, $3, $4, $5::jsonb, $6) RETURNING *",
            c.Me().Id, codigo, rol, expiraEn, JsonX.Arr(permisos).ToJsonString(), rolLabel);
        return Api.Data(r.First, 201);
    }

    static async Task<IResult> Listar(HttpContext c)
    {
        var r = await Db.Query(
            @"SELECT ci.*,
                     u.nombre_completo AS usado_por_nombre
              FROM codigos_invitacion ci
              LEFT JOIN usuarios u ON u.id = ci.usado_por
              WHERE ci.admin_id = $1
              ORDER BY ci.created_at DESC", c.Me().Id);
        return Api.Data(r.Rows);
    }

    static async Task<IResult> Revocar(HttpContext c)
    {
        var r = await Db.Query("DELETE FROM codigos_invitacion WHERE id = $1 AND admin_id = $2 RETURNING id",
            Api.Route(c, "id"), c.Me().Id);
        if (r.First == null) throw new AppError("Código no encontrado", 404);
        return Api.Msg("Código revocado");
    }
}
