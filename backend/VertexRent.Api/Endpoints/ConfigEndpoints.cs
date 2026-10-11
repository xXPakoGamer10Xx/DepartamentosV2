using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VertexRent.Api;

public static class ConfigEndpoints
{
    static readonly string[] Allowed =
    {
        "arrendador_nombre", "arrendador_direccion", "arrendador_rfc", "arrendador_telefono",
        "banco_nombre", "banco_clabe", "banco_titular",
        "admin_invite_code", "app_url",
        "dias_gracia_retraso",
        "usa_qr_inquilinos", "recordatorios_renta_dias",
    };

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/config").Auth();

        g.MapGet("/", H(GetConfig));
        g.MapPut("/", H(UpdateConfig)).AdminOnly();
    }

    static async Task<IResult> GetConfig(HttpContext c)
    {
        var u = c.Me();
        var adminId = u.OwnerId;
        string? deptoNumero = null;

        if (u.Rol == "inquilino")
        {
            var inq = await Db.Query("SELECT admin_id, depto_numero FROM inquilinos WHERE usuario_id = $1 AND estado = 'activo' LIMIT 1", u.Id);
            if (inq.First == null) return Api.Data(new Dictionary<string, object?>());
            adminId = inq.First.S("admin_id")!;
            deptoNumero = inq.First.S("depto_numero");
        }
        else if (Api.Query(c, "depto_numero") is { } dn)
        {
            // El admin puede pedir la cuenta bancaria resuelta para un depto (pantalla "Cobrar Renta").
            deptoNumero = dn;
        }

        var r = await Db.Query("SELECT clave, valor FROM configuracion WHERE admin_id = $1 ORDER BY clave", adminId);
        var config = new Dictionary<string, object?>();
        foreach (var row in r.Rows)
            config[row.S("clave")!] = row.S("valor");

        // Cuenta bancaria que aplica para el depto (asignada o, si no, la predeterminada).
        if (deptoNumero != null)
        {
            var cuenta = await Db.Query(
                @"SELECT cb.banco_nombre, cb.banco_clabe, cb.banco_titular
                  FROM cuentas_bancarias cb
                  WHERE cb.admin_id = $1 AND cb.activo = TRUE
                    AND (
                      cb.id = (SELECT d.cuenta_bancaria_id FROM departamentos d
                               WHERE d.admin_id = $1 AND d.numero = $2)
                      OR cb.es_predeterminada = TRUE
                    )
                  ORDER BY (cb.id = (SELECT d.cuenta_bancaria_id FROM departamentos d
                                     WHERE d.admin_id = $1 AND d.numero = $2)) DESC
                  LIMIT 1", adminId, deptoNumero);
            if (cuenta.First is { } cu)
            {
                config["banco_nombre"] = cu.S("banco_nombre") ?? "";
                config["banco_clabe"] = cu.S("banco_clabe") ?? "";
                config["banco_titular"] = cu.S("banco_titular") ?? "";
            }
        }
        return Api.Data(config);
    }

    static async Task<IResult> UpdateConfig(HttpContext c)
    {
        var u = c.Me();
        if (u.Rol != "admin") throw new AppError("No autorizado", 403);
        var updates = await Api.Body(c);

        foreach (var (clave, node) in updates.ToList())
        {
            if (!Allowed.Contains(clave)) continue;
            var raw = Js.Str(node) ?? "null";

            if (clave == "dias_gracia_retraso")
            {
                var dias = ReportesEndpoints.ParseInt(raw);
                if (dias is null or < 0 or > 90) throw new AppError("Los días de gracia deben ser un número entre 0 y 90", 400);
            }
            if (clave == "recordatorios_renta_dias")
            {
                var t = raw.Trim();
                if (t != "")
                {
                    var partes = t.Split(',').Select(s => s.Trim()).ToArray();
                    var valido = partes.Length <= 6 && partes.All(s => Regex.IsMatch(s, @"^\d{1,2}$") && int.Parse(s) <= 30);
                    if (!valido) throw new AppError("Recordatorios inválidos: hasta 6 valores entre 0 y 30 días, separados por coma", 400);
                }
            }

            var valor = raw;
            // Normaliza la lista de recordatorios: enteros únicos, orden descendente.
            if (clave == "recordatorios_renta_dias" && raw.Trim() != "")
                valor = string.Join(",", raw.Split(',').Select(s => ReportesEndpoints.ParseInt(s.Trim())).Where(n => n != null)
                    .Select(n => n!.Value).Distinct().OrderByDescending(n => n));

            await Db.Query(
                @"INSERT INTO configuracion (admin_id, clave, valor) VALUES ($1, $2, $3)
                  ON CONFLICT (admin_id, clave) DO UPDATE SET valor = $3, updated_at = NOW()", u.Id, clave, valor);
        }
        return Api.Msg("Configuración actualizada");
    }
}
