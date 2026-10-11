using System.Globalization;
using System.Text.RegularExpressions;

namespace VertexRent.Api;

public static class ReportesEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/reportes").Auth().Permiso("reportes");
        g.MapGet("/anual", H(Anual));
        g.MapGet("/mensual", H(Mensual));
    }

    /// <summary>parseInt(String(x), 10): entero inicial o null (NaN).</summary>
    internal static int? ParseInt(string? s)
    {
        if (s == null) return null;
        var m = Regex.Match(s.TrimStart(), @"^[+-]?\d+");
        return m.Success && int.TryParse(m.Value, out var n) ? n : null;
    }

    static int ParseYear(HttpContext c)
    {
        var y = ParseInt(Api.Query(c, "year"));
        if (y is null or 0 or < 2000 or > 2100) throw new AppError("year inválido", 400);
        return y.Value;
    }

    static async Task<IResult> Anual(HttpContext c)
    {
        var year = ParseYear(c);
        var adminId = c.Me().OwnerId;

        var renta = Db.Query(
            @"SELECT COALESCE(SUM(a.monto), 0) AS total
              FROM abonos_pago a
              JOIN pagos p ON p.id = a.pago_id
              JOIN inquilinos i ON i.id = p.inquilino_id
              WHERE i.admin_id = $1 AND EXTRACT(YEAR FROM a.fecha) = $2", adminId, year);
        var extra = Db.Query(
            @"SELECT COALESCE(SUM(c.monto), 0) AS total
              FROM cuotas_extra c
              JOIN inquilinos i ON i.id = c.inquilino_id
              WHERE i.admin_id = $1 AND c.estado = 'pagado' AND EXTRACT(YEAR FROM c.pagado_en) = $2", adminId, year);
        var deposito = Db.Query(
            @"SELECT COALESCE(SUM(ad.monto), 0) AS total
              FROM abonos_deposito ad
              JOIN inquilinos i ON i.id = ad.inquilino_id
              WHERE i.admin_id = $1 AND EXTRACT(YEAR FROM ad.fecha) = $2", adminId, year);
        await Task.WhenAll(renta, extra, deposito);

        var r = (double)renta.Result.First!.D("total");
        var e = (double)extra.Result.First!.D("total");
        var d = (double)deposito.Result.First!.D("total");
        return Api.Data(new Dictionary<string, object?>
        {
            ["year"] = year, ["renta_total"] = r, ["extra_total"] = e,
            ["deposito_total"] = d, ["total_general"] = r + e + d,
        });
    }

    // Ingreso real mes a mes y por departamento, contabilizado por la FECHA en que entró el dinero.
    static async Task<IResult> Mensual(HttpContext c)
    {
        var year = ParseYear(c);
        var adminId = c.Me().OwnerId;

        var detalleTask = Db.Query(
            @"WITH ingresos AS (
                SELECT i.depto_numero, EXTRACT(MONTH FROM a.fecha)::int AS mes, 'renta' AS tipo, a.monto
                FROM abonos_pago a
                JOIN pagos p ON p.id = a.pago_id
                JOIN inquilinos i ON i.id = p.inquilino_id
                WHERE i.admin_id = $1 AND EXTRACT(YEAR FROM a.fecha) = $2
                UNION ALL
                SELECT i.depto_numero, EXTRACT(MONTH FROM c.pagado_en)::int, 'extra', c.monto
                FROM cuotas_extra c
                JOIN inquilinos i ON i.id = c.inquilino_id
                WHERE i.admin_id = $1 AND c.estado = 'pagado' AND EXTRACT(YEAR FROM c.pagado_en) = $2
                UNION ALL
                SELECT i.depto_numero, EXTRACT(MONTH FROM ad.fecha)::int, 'deposito', ad.monto
                FROM abonos_deposito ad
                JOIN inquilinos i ON i.id = ad.inquilino_id
                WHERE i.admin_id = $1 AND EXTRACT(YEAR FROM ad.fecha) = $2
              )
              SELECT depto_numero, mes, tipo, SUM(monto) AS total
              FROM ingresos
              GROUP BY depto_numero, mes, tipo", adminId, year);
        var aniosTask = Db.Query(
            @"SELECT DISTINCT y FROM (
                SELECT EXTRACT(YEAR FROM a.fecha)::int AS y
                FROM abonos_pago a JOIN pagos p ON p.id = a.pago_id JOIN inquilinos i ON i.id = p.inquilino_id
                WHERE i.admin_id = $1
                UNION
                SELECT EXTRACT(YEAR FROM ad.fecha)::int
                FROM abonos_deposito ad JOIN inquilinos i ON i.id = ad.inquilino_id
                WHERE i.admin_id = $1
                UNION
                SELECT EXTRACT(YEAR FROM c.pagado_en)::int
                FROM cuotas_extra c JOIN inquilinos i ON i.id = c.inquilino_id
                WHERE i.admin_id = $1 AND c.pagado_en IS NOT NULL
              ) t WHERE y IS NOT NULL ORDER BY y DESC", adminId);
        await Task.WhenAll(detalleTask, aniosTask);

        var tipos = new[] { "renta", "extra", "deposito" };
        var porTipo = tipos.ToDictionary(t => t, _ => new double[12]);
        var totalMes = new double[12];
        var deptos = new SortedDictionary<int, (double total, double[] meses)>();

        foreach (var row in detalleTask.Result.Rows)
        {
            var mesIdx = row.I("mes") - 1;
            var monto = (double)row.D("total");
            if (mesIdx < 0 || mesIdx > 11) continue;

            porTipo[row.S("tipo")!][mesIdx] += monto;
            totalMes[mesIdx] += monto;

            var dn = row.I("depto_numero");
            if (!deptos.TryGetValue(dn, out var d)) d = (0, new double[12]);
            d.total += monto;
            d.meses[mesIdx] += monto;
            deptos[dn] = d;
        }

        var meses = Enumerable.Range(0, 12).Select(i => new Dictionary<string, object?>
        {
            ["mes"] = i + 1, ["renta"] = porTipo["renta"][i], ["extra"] = porTipo["extra"][i],
            ["deposito"] = porTipo["deposito"][i], ["total"] = totalMes[i],
        }).ToList();
        var porDepto = deptos.Select(kv => new Dictionary<string, object?>
        {
            ["depto_numero"] = kv.Key, ["total"] = kv.Value.total, ["meses"] = kv.Value.meses,
        }).ToList();

        return Api.Data(new Dictionary<string, object?>
        {
            ["year"] = year, ["meses"] = meses, ["por_departamento"] = porDepto,
            ["total_anual"] = totalMes.Sum(),
            ["anios_disponibles"] = aniosTask.Result.Rows.Select(r => r.I("y")).ToList(),
        });
    }
}
