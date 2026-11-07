namespace VertexRent.Api;

/// <summary>Cálculo de los reportes anual y mensual. El SQL vive en <see cref="ReportesRepository"/>.</summary>
public static class ReportesService
{
    public static int ValidarAnio(string? crudo)
    {
        var y = Js.ParseInt(crudo);
        if (y is null or 0 or < 2000 or > 2100) throw new AppError("year inválido", 400);
        return y.Value;
    }

    public static async Task<Dictionary<string, object?>> Anual(AuthUser u, int year)
    {
        var renta = ReportesRepository.TotalRenta(u.OwnerId, year);
        var extra = ReportesRepository.TotalExtra(u.OwnerId, year);
        var deposito = ReportesRepository.TotalDeposito(u.OwnerId, year);
        await Task.WhenAll(renta, extra, deposito);
        double r = (double)renta.Result, e = (double)extra.Result, d = (double)deposito.Result;
        return new() { ["year"] = year, ["renta_total"] = r, ["extra_total"] = e, ["deposito_total"] = d, ["total_general"] = r + e + d };
    }

    /// <summary>Ingreso real mes a mes y por departamento.</summary>
    public static async Task<Dictionary<string, object?>> Mensual(AuthUser u, int year)
    {
        var detalleTarea = ReportesRepository.DetalleMensual(u.OwnerId, year);
        var aniosTarea = ReportesRepository.AniosConIngresos(u.OwnerId);
        await Task.WhenAll(detalleTarea, aniosTarea);

        var tipos = new[] { "renta", "extra", "deposito" };
        var porTipo = tipos.ToDictionary(t => t, _ => new double[12]);
        var totalMes = new double[12];
        var deptos = new SortedDictionary<int, (double total, double[] meses)>();

        foreach (var fila in detalleTarea.Result)
        {
            var mes = fila.I("mes") - 1;
            if (mes < 0 || mes > 11) continue;
            var monto = (double)fila.D("total");
            porTipo[fila.S("tipo")!][mes] += monto;
            totalMes[mes] += monto;

            var depto = fila.I("depto_numero");
            if (!deptos.TryGetValue(depto, out var d)) d = (0, new double[12]);
            d.total += monto;
            d.meses[mes] += monto;
            deptos[depto] = d;
        }

        return new()
        {
            ["year"] = year,
            ["meses"] = Enumerable.Range(0, 12).Select(i => new Dictionary<string, object?>
            {
                ["mes"] = i + 1, ["renta"] = porTipo["renta"][i], ["extra"] = porTipo["extra"][i],
                ["deposito"] = porTipo["deposito"][i], ["total"] = totalMes[i],
            }).ToList(),
            ["por_departamento"] = deptos.Select(kv => new Dictionary<string, object?>
            {
                ["depto_numero"] = kv.Key, ["total"] = kv.Value.total, ["meses"] = kv.Value.meses,
            }).ToList(),
            ["total_anual"] = totalMes.Sum(),
            ["anios_disponibles"] = aniosTarea.Result,
        };
    }
}
