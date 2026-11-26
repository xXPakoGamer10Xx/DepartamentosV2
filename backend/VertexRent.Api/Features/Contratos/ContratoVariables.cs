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
        new("deposito", "Depósito", GPagos, "4,500.00"),
        new("deposito_letra", "Depósito con letra", GPagos, "CUATRO MIL QUINIENTOS PESOS 00/100 M.N."),
        new("deposito_tipo", "Tipo de depósito", GPagos, "único"),
        new("deposito_fechas", "Fechas del depósito", GPagos, "01 de enero de 2025"),
        new("deposito_modalidad", "Modalidad del depósito", GPagos, "Opción 3: Contado y beneficio (Mes 13)"),
        new("deposito_desglose", "Desglose de pagos del depósito", GPagos, "Pago único de $4,500.00 al inicio del contrato"),
        new("clausula_deposito", "Cláusula completa del depósito", GPagos, "El arrendatario cubrirá por concepto de depósito la cantidad de $4,500.00..."),
        new("fecha_pago", "Día de pago", GPagos, "01 de cada mes"),
        new("dia_pago_numero", "Día de pago (número)", GPagos, "1"),
        new("metodo_pago", "Forma de pago", GPagos, "transferencia"),
        new("banco_nombre", "Banco", GPagos, "BBVA"),
        new("banco_clabe", "CLABE", GPagos, "012180001234567890"),
        new("banco_titular", "Titular de la cuenta", GPagos, "Roberto González"),

        new("fecha_actual", "Fecha del contrato (día que entró)", GFechas, "01 de enero de 2025"),
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
        var rawTipo = data.Str("deposito_tipo", "ninguno").Trim().ToLowerInvariant();
        var montoSemanal = data.Num("deposito_monto_semanal") ?? 1000m;
        var fechasFormateadas = fechas
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => ContractFormat.LongDate(f, true))
            .Where(f => f.Length > 0)
            .ToList();
        var (depTipoTexto, depModalidad, depDesglose, clausulaDeposito) =
            ConstruirTextosDeposito(rawTipo, deposito, montoSemanal, fechas.Count, fechasFormateadas);
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
            ["deposito_tipo"] = depTipoTexto,
            ["deposito_fechas"] = fechasFormateadas.Count > 0 ? string.Join(", ", fechasFormateadas) : "",
            ["deposito_modalidad"] = depModalidad,
            ["deposito_desglose"] = depDesglose,
            ["clausula_deposito"] = clausulaDeposito,
            ["fecha_pago"] = fechaPago,
            ["dia_pago_numero"] = diaPago.Success ? int.Parse(diaPago.Value).ToString() : "",
            ["metodo_pago"] = data.Str("metodo_pago", "efectivo"),
            ["banco_nombre"] = data.Str("banco_nombre"),
            ["banco_clabe"] = data.Str("banco_clabe"),
            ["banco_titular"] = data.Str("banco_titular"),

            // La fecha del contrato es el día en que entró el inquilino, no el día en que se genera el archivo.
            ["fecha_actual"] = string.IsNullOrWhiteSpace(data.S("fecha_inicio")) ? ContractFormat.Today(true) : ContractFormat.LongDate(data.S("fecha_inicio"), true),
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

    static (string Tipo, string Modalidad, string Desglose, string Clausula) ConstruirTextosDeposito(
        string rawTipo, decimal? deposito, decimal montoSemanal, int cantidadFechas, List<string> fechasFormateadas)
    {
        if (deposito is null or <= 0)
        {
            return (
                "sin depósito",
                "Sin depósito",
                "No aplica",
                "Es importante que al desocupar el departamento el arrendatario avise con un mínimo de 15 días naturales de anticipación. En el presente contrato no se pacta entrega de depósito."
            );
        }

        var depTipoTexto = rawTipo switch
        {
            "contado_beneficio" => "contado y beneficio (mes 13)",
            "diferido_mensual" or "personalizado" => cantidadFechas > 0
                ? $"pago diferido en {cantidadFechas} mensualidades"
                : "pago diferido en mensualidades",
            "quincenal" or "quincenas" => "pago quincenal en 2 exhibiciones",
            "semanal" => $"pago semanal (${Dinero(montoSemanal)} por semana)",
            "fechas" => cantidadFechas > 0 ? $"en {cantidadFechas} fecha(s) acordada(s)" : "fechas acordadas",
            _ => "pago único al inicio",
        };

        string modalidad;
        string desglosePago;
        switch (rawTipo)
        {
            case "diferido_mensual":
            case "personalizado":
            {
                var n = Math.Max(cantidadFechas, 1);
                var cuota = Math.Round(deposito.Value / n, 2, MidpointRounding.AwayFromZero);
                modalidad = $"Opción 1: Pago diferido en {n} mensualidades";
                desglosePago = $"en {n} parcialidades mensuales de ${Dinero(cuota)} M.N. adicionales a su renta mensual"
                    + (fechasFormateadas.Count > 0 ? $", pagaderas los días: {string.Join(", ", fechasFormateadas)}" : "");
                break;
            }
            case "quincenal":
            {
                var mitad = Math.Round(deposito.Value / 2m, 2, MidpointRounding.AwayFromZero);
                modalidad = "Opción 2: Pago quincenal intercalado";
                desglosePago = $"en 2 pagos quincenales intercalados de ${Dinero(mitad)} M.N. cada uno (a los 15 y 45 días del inicio del contrato)"
                    + (fechasFormateadas.Count > 0 ? $", correspondientes al {string.Join(" y ", fechasFormateadas)}" : "");
                break;
            }
            case "quincenas":
            {
                var mitad = Math.Round(deposito.Value / 2m, 2, MidpointRounding.AwayFromZero);
                modalidad = "Pago en dos quincenas";
                desglosePago = $"en 2 exhibiciones quincenales de ${Dinero(mitad)} M.N."
                    + (fechasFormateadas.Count > 0 ? $" los días {string.Join(" y ", fechasFormateadas)}" : "");
                break;
            }
            case "semanal":
                modalidad = $"Opción 4: Pago semanal (${Dinero(montoSemanal)} por semana)";
                desglosePago = $"mediante abonos semanales constantes de ${Dinero(montoSemanal)} M.N., con los cuales se cubre en primer lugar la renta mensual vigente y el excedente se acumula hasta completar el total del depósito";
                break;
            case "fechas":
                modalidad = "Fechas personalizadas acordadas";
                desglosePago = fechasFormateadas.Count > 0
                    ? $"en {fechasFormateadas.Count} pago(s) en las fechas: {string.Join(", ", fechasFormateadas)}"
                    : "en las fechas pactadas entre las partes";
                break;
            case "contado_beneficio":
                modalidad = "Opción 3: Contado y beneficio (Mes 13)";
                desglosePago = "en una sola exhibición de contado al inicio del presente contrato";
                break;
            default:
                modalidad = "Pago único al inicio del contrato";
                desglosePago = "en una sola exhibición de contado al inicio del presente contrato";
                break;
        }

        var desgloseCapitalizado = char.ToUpper(desglosePago[0]) + desglosePago[1..];
        var reglaMes13 = rawTipo == "contado_beneficio"
            ? "Al haberse cubierto el primer mes de renta y el depósito de contado al inicio (Opción 3), únicamente si el arrendatario cumple íntegramente el plazo pactado en este contrato sin adeudos y avisa con al menos 15 días de anticipación que no desea renovar, el depósito fungirá en especie como pago de renta del mes 13, sin que en ningún caso proceda devolución en efectivo."
            : "En la modalidad de pago elegida no aplica el beneficio de mes 13, por lo que el depósito no funge como renta de ningún mes ni al término del contrato.";

        var clausula =
            $"El arrendatario se obliga a cubrir por concepto de depósito la cantidad de ${Dinero(deposito)} ({ContractFormat.MontoALetras(deposito.Value)}), pagadera {desglosePago}. "
            + "Para desocupar el departamento el arrendatario deberá notificar con un mínimo de 15 días naturales de anticipación. El monto entregado o abonado por concepto de depósito NO es reembolsable bajo ninguna circunstancia (ni al cumplir el contrato ni si se desocupa antes), por lo que en ninguna opción existe devolución de dinero en efectivo ni de abonos parciales. "
            + reglaMes13;

        return (depTipoTexto, modalidad, desgloseCapitalizado, clausula);
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
