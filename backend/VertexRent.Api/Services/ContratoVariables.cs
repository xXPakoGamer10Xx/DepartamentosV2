using System.Text.RegularExpressions;

namespace VertexRent.Api;

/// <summary>Variable que se puede usar en una plantilla de contrato como {{key}}.</summary>
public sealed record VarDef(string Key, string Etiqueta, string Grupo, string Ejemplo);

/// <summary>
/// ÚNICA fuente de verdad de las variables de contrato: el frontend, el prompt de la IA, el relleno de
/// plantillas y los datos de ejemplo salen todos de aquí.
/// </summary>
public static class ContratoVariables
{
    public const string GInquilino = "Inquilino", GPropiedad = "Propiedad", GArrendador = "Arrendador",
        GPagos = "Renta y pagos", GFechas = "Fechas", GFiador = "Fiador", GOtros = "Otros";

    public static readonly IReadOnlyList<VarDef> Todas = new List<VarDef>
    {
        new("nombre_completo", "Nombre del inquilino", GInquilino, "Juan Pérez García"),
        new("tel_arrendatario", "Teléfono del inquilino", GInquilino, "55 1234 5678"),
        new("inquilino_email", "Correo del inquilino", GInquilino, "juan@correo.com"),
        new("inquilino_domicilio", "Domicilio del inquilino", GInquilino, "Av. Reforma 45, Col. Centro, CDMX"),
        new("inquilino_identificacion", "Identificación del inquilino", GInquilino, "INE 1234567890123"),

        new("depto_numero", "Número de departamento", GPropiedad, "3"),
        new("propiedad_direccion", "Dirección de la propiedad", GPropiedad, "Calle Principal 123, Col. Centro"),
        new("propiedad_descripcion", "Descripción de la propiedad", GPropiedad, "Departamento de 2 recámaras"),
        new("inventario", "Inventario (una línea por objeto)", GPropiedad, "Refrigerador\nEstufa\nLavadora"),
        new("inventario_lista", "Inventario con viñetas", GPropiedad, "• Refrigerador\n• Estufa\n• Lavadora"),

        new("arrendador_nombre", "Nombre del arrendador", GArrendador, "Roberto González"),
        new("arrendador_direccion", "Dirección del arrendador", GArrendador, "Calle Principal 123, Col. Centro"),
        new("arrendador_rfc", "RFC del arrendador", GArrendador, "GORO800101ABC"),
        new("arrendador_telefono", "Teléfono del arrendador", GArrendador, "55 8765 4321"),

        new("renta", "Renta mensual", GPagos, "4,500.00"),
        new("renta_letra", "Renta con letra", GPagos, "CUATRO MIL QUINIENTOS PESOS 00/100 M.N."),
        new("deposito", "Depósito / compensación", GPagos, "4,500.00"),
        new("deposito_letra", "Depósito con letra", GPagos, "CUATRO MIL QUINIENTOS PESOS 00/100 M.N."),
        new("deposito_tipo", "Tipo de depósito", GPagos, "único"),
        new("deposito_fechas", "Fechas del depósito", GPagos, "01 de enero de 2025"),
        new("fecha_pago", "Día de pago", GPagos, "01 de cada mes"),
        new("dia_pago_numero", "Día de pago (número)", GPagos, "1"),
        new("metodo_pago", "Forma de pago", GPagos, "transferencia"),
        new("banco_nombre", "Banco", GPagos, "BBVA"),
        new("banco_clabe", "CLABE", GPagos, "012180001234567890"),
        new("banco_titular", "Titular de la cuenta", GPagos, "Roberto González"),

        new("fecha_actual", "Fecha de hoy", GFechas, "05 de enero de 2025"),
        new("fecha_inicio", "Inicio del contrato", GFechas, "01 de enero de 2025"),
        new("fecha_termino", "Fin del contrato", GFechas, "31 de diciembre de 2025"),
        new("duracion_meses", "Duración (meses)", GFechas, "12"),

        new("fiador_nombre", "Nombre del fiador", GFiador, "María García López"),
        new("fiador_telefono", "Teléfono del fiador", GFiador, "55 8765 4321"),
        new("fiador_domicilio", "Domicilio del fiador", GFiador, "Calle Olmo 8, Col. Jardines"),

        new("observaciones", "Observaciones", GOtros, "Sin observaciones adicionales"),
        new("arrendatario_linea", "Línea de firma del arrendatario", GOtros, "ARRENDATARIO: Juan Pérez García"),
        new("arrendador_linea", "Línea de firma del arrendador", GOtros, "ARRENDADORA: Roberto González"),
        new("fiador_linea", "Línea de firma del fiador", GOtros, "FIADOR: María García López"),
        new("firma_arrendatario", "Bloque de firma del arrendatario", GOtros, "Juan Pérez García\nTel: 55 1234 5678\nArrendatario"),
        new("firma_arrendador", "Bloque de firma del arrendador", GOtros, "Roberto González\nArrendador"),
        new("firma_fiador", "Bloque de firma del fiador", GOtros, "María García López\nTel: 55 8765 4321\nFiador"),
    };

    public static readonly HashSet<string> Claves = Todas.Select(v => v.Key).ToHashSet();

    // Alias que siguen funcionando en plantillas antiguas pero no se ofrecen en el catálogo.
    static readonly string[] Alias = { "renta_numero", "deposito_numero" };

    public static bool Existe(string key) => Claves.Contains(key) || Alias.Contains(key);

    static string Dinero(decimal? n) => n == null ? "0.00" : ContractFormat.Number2(n.Value);

    /// <summary>Valores ya formateados para rellenar una plantilla. `data` = fila del inquilino + datos del contexto.</summary>
    public static Dictionary<string, string> Build(ContractData data)
    {
        var telArr = !string.IsNullOrEmpty(data.S("tel_arrendatario")) ? $" — Tel: {data.S("tel_arrendatario")}" : "";
        var telFia = !string.IsNullOrEmpty(data.S("fiador_telefono")) ? $" — Tel: {data.S("fiador_telefono")}" : "";
        var inv = data.List("inventario");
        var inventario = string.Join("\n", inv.Count > 0 ? inv : data.List("inventario_base"));
        var fechas = data.List("deposito_fechas");
        var fiador = data.S("fiador_nombre");
        var nombre = data.Str("nombre_completo");
        var renta = data.Num("renta");
        var deposito = data.Num("deposito");
        var arrNombre = data.Str("arrendador_nombre");
        var direccionArr = data.Str("arrendador_direccion");
        var fechaPago = data.Str("fecha_pago");
        var diaPago = Regex.Match(fechaPago, @"\d{1,2}");

        var inicio = ContractFormat.ParseCalendarDate(data.S("fecha_inicio"));
        var fin = ContractFormat.ParseCalendarDate(data.S("fecha_termino"));
        var meses = inicio != null && fin != null
            ? Math.Max(0, (int)Math.Round(((fin.Value - inicio.Value).TotalDays + 1) / 30.4375))
            : (int?)null;

        var rentaLetra = data.Str("renta_letra");
        if (rentaLetra.Length == 0 && renta != null) rentaLetra = ContractFormat.MontoALetras(renta.Value);
        else if (rentaLetra.Length > 0 && !rentaLetra.Contains("/100"))
            rentaLetra += (rentaLetra.Contains("peso", StringComparison.OrdinalIgnoreCase) ? "" : " PESOS") + " 00/100 M.N.";

        var v = new Dictionary<string, string>
        {
            ["nombre_completo"] = nombre,
            ["tel_arrendatario"] = data.Str("tel_arrendatario"),
            ["inquilino_email"] = data.Str("email_contacto"),
            ["inquilino_domicilio"] = data.Str("domicilio"),
            ["inquilino_identificacion"] = data.Str("identificacion"),

            ["depto_numero"] = data.Str("depto_numero"),
            ["propiedad_direccion"] = data.Str("propiedad_direccion", direccionArr),
            ["propiedad_descripcion"] = data.Str("propiedad_descripcion"),
            ["inventario"] = inventario,
            ["inventario_lista"] = string.Join("\n", (inv.Count > 0 ? inv : data.List("inventario_base")).Select(i => "• " + i)),

            ["arrendador_nombre"] = arrNombre,
            ["arrendador_direccion"] = direccionArr,
            ["arrendador_rfc"] = data.Str("arrendador_rfc"),
            ["arrendador_telefono"] = data.Str("arrendador_telefono"),

            ["renta"] = Dinero(renta),
            ["renta_numero"] = data.Str("renta"),
            ["renta_letra"] = rentaLetra,
            ["deposito"] = Dinero(deposito),
            ["deposito_numero"] = data.Str("deposito"),
            ["deposito_letra"] = deposito is > 0 ? ContractFormat.MontoALetras(deposito.Value) : "",
            ["deposito_tipo"] = data.Str("deposito_tipo", "ninguno"),
            ["deposito_fechas"] = fechas.Count > 0 ? string.Join(", ", fechas.Select(f => ContractFormat.LongDate(f, true))) : "",
            ["fecha_pago"] = fechaPago,
            ["dia_pago_numero"] = diaPago.Success ? int.Parse(diaPago.Value).ToString() : "",
            ["metodo_pago"] = data.Str("metodo_pago", "efectivo"),
            ["banco_nombre"] = data.Str("banco_nombre"),
            ["banco_clabe"] = data.Str("banco_clabe"),
            ["banco_titular"] = data.Str("banco_titular"),

            ["fecha_actual"] = ContractFormat.Today(true),
            ["fecha_inicio"] = ContractFormat.LongDate(data.S("fecha_inicio"), true),
            ["fecha_termino"] = ContractFormat.LongDate(data.S("fecha_termino"), true),
            ["duracion_meses"] = meses?.ToString() ?? "",

            ["fiador_nombre"] = data.Str("fiador_nombre"),
            ["fiador_telefono"] = data.Str("fiador_telefono"),
            ["fiador_domicilio"] = data.Str("fiador_domicilio"),

            ["observaciones"] = data.Str("observaciones"),
            // Líneas de firma: quedan vacías (y su párrafo se elimina) cuando no hay datos.
            ["arrendatario_linea"] = $"ARRENDATARIO: {nombre}{telArr}",
            ["arrendador_linea"] = $"ARRENDADORA: {arrNombre}",
            ["fiador_linea"] = !string.IsNullOrEmpty(fiador) ? $"FIADOR: {fiador}{telFia}" : "",
            // Bloques de firma de varias líneas (los saltos de línea se respetan al rellenar el Word).
            ["firma_arrendatario"] = nombre + (string.IsNullOrEmpty(data.S("tel_arrendatario")) ? "" : $"\nTel: {data.S("tel_arrendatario")}") + "\nArrendatario",
            ["firma_arrendador"] = arrNombre + "\nArrendador",
            ["firma_fiador"] = !string.IsNullOrEmpty(fiador) ? fiador + (string.IsNullOrEmpty(data.S("fiador_telefono")) ? "" : $"\nTel: {data.S("fiador_telefono")}") + "\nFiador" : "",
        };
        return v;
    }

    /// <summary>Datos de ejemplo para probar una plantilla sin un inquilino real.</summary>
    public static Dictionary<string, object?> Demo() => new()
    {
        ["nombre_completo"] = "Juan Pérez García",
        ["depto_numero"] = 3,
        ["renta"] = 4500,
        ["deposito"] = 4500,
        ["deposito_tipo"] = "único",
        ["fecha_inicio"] = "2025-01-01",
        ["fecha_termino"] = "2025-12-31",
        ["fecha_pago"] = "01 de cada mes",
        ["tel_arrendatario"] = "55 1234 5678",
        ["email_contacto"] = "juan@correo.com",
        ["domicilio"] = "Av. Reforma 45, Col. Centro, CDMX",
        ["identificacion"] = "INE 1234567890123",
        ["fiador_nombre"] = "María García López",
        ["fiador_telefono"] = "55 8765 4321",
        ["fiador_domicilio"] = "Calle Olmo 8, Col. Jardines",
        ["arrendador_nombre"] = "Roberto González",
        ["arrendador_direccion"] = "Calle Principal 123, Col. Centro",
        ["arrendador_rfc"] = "GORO800101ABC",
        ["arrendador_telefono"] = "55 8765 4321",
        ["propiedad_direccion"] = "Calle Principal 123, Col. Centro",
        ["propiedad_descripcion"] = "Departamento de 2 recámaras",
        ["metodo_pago"] = "transferencia",
        ["banco_nombre"] = "BBVA",
        ["banco_clabe"] = "012180001234567890",
        ["banco_titular"] = "Roberto González",
        ["observaciones"] = "Sin observaciones adicionales",
        ["inventario_base"] = new List<string> { "Refrigerador", "Estufa", "Lavadora" },
    };
}
