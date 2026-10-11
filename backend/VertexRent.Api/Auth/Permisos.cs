namespace VertexRent.Api;

// Catálogo de permisos para colaboradores (rol 'cobrador'). El admin siempre tiene todo.
public static class Permisos
{
    public static readonly string[] Todos =
    {
        "pagos", "pagos.marcar", "inquilinos", "inquilinos.editar", "departamentos",
        "contratos", "reportes", "cuentas", "tickets", "notificaciones.pagos",
    };

    public sealed record Preset(string Label, string Descripcion, string[] Permisos);

    public static readonly Dictionary<string, Preset> Presets = new()
    {
        ["cobrador"] = new("Cobrador", "Registra pagos y recibe avisos de cobranza.",
            new[] { "pagos", "pagos.marcar", "inquilinos", "notificaciones.pagos" }),
        ["contador"] = new("Contador", "Ve pagos, reportes y cuentas — solo lectura.",
            new[] { "pagos", "reportes", "cuentas", "inquilinos", "departamentos" }),
        ["asistente"] = new("Asistente", "Administra inquilinos, departamentos y contratos.",
            new[] { "inquilinos", "inquilinos.editar", "departamentos", "contratos", "tickets" }),
    };

    public static List<string> Sanitize(System.Text.Json.Nodes.JsonNode? input)
    {
        var res = new List<string>();
        if (input is not System.Text.Json.Nodes.JsonArray arr) return res;
        var set = new HashSet<string>(Todos);
        foreach (var x in arr)
        {
            var s = x is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var str) ? str : null;
            if (s != null && set.Contains(s) && !res.Contains(s)) res.Add(s);
        }
        return res;
    }
}
