using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VertexRent.Api;

/// <summary>Reglas del módulo de inquilinos. No conoce HTTP; el SQL vive en <see cref="InquilinosRepository"/>.</summary>
public static class InquilinosService
{
    // ── Lectura de campos del cuerpo ─────────────────────────────────────────
    /// <summary>Texto opcional: vacío → null; máximo 500 caracteres.</summary>
    static string? Texto(JsonObject b, string k) => Js.Str(b[k])?.Trim() is { Length: > 0 } t ? (t.Length > 500 ? t[..500] : t) : null;
    /// <summary>En una edición, si el campo no viene se conserva el valor actual; si viene vacío se borra.</summary>
    static string? TextoUpd(JsonObject b, string k, Row cur) => b.ContainsKey(k) ? Texto(b, k) : cur.S(k);

    /// <summary>Primer valor "con contenido" entre varias claves (el front envía snake_case o camelCase).</summary>
    static JsonNode? Primero(JsonObject b, params string[] keys) => Js.Or(keys.Select(k => b[k]).ToArray());

    // ── Consultas ────────────────────────────────────────────────────────────
    public static Task<Row?> MiDepto(AuthUser u) => InquilinosRepository.MiDepto(u.Id);

    public static Task<QueryResult> Listar(AuthUser u, string? search, string? estado, string? depto) =>
        InquilinosRepository.Listar(u.Id, search, estado, depto);

    public static async Task<Row> PorId(AuthUser u, string id) =>
        await InquilinosRepository.PorIdConHistorial(u.Id, id) ?? throw new AppError("Inquilino no encontrado", 404);

    // ── Alta ─────────────────────────────────────────────────────────────────
    public static async Task<Row> Crear(AuthUser u, string? ip, JsonObject b)
    {
        var depTipo = Js.T(Primero(b, "deposito_tipo", "depositoTipo")) ? Primero(b, "deposito_tipo", "depositoTipo") : JsonValue.Create("ninguno");
        var depFechas = Js.T(Primero(b, "deposito_fechas", "depositoFechas")) ? Primero(b, "deposito_fechas", "depositoFechas")! : new JsonArray();
        var metodo = Js.T(Primero(b, "metodo_pago", "metodoPago")) ? Primero(b, "metodo_pago", "metodoPago") : JsonValue.Create("efectivo");

        var nombre = Primero(b, "nombre_completo", "nombre");
        var depto = Primero(b, "depto_numero", "depto");
        var rentaLetra = Primero(b, "renta_letra", "rentaLetra");
        var fechaInicio = Primero(b, "fecha_inicio", "fechaInicio");
        var fechaTermino = Primero(b, "fecha_termino", "fechaTermino");
        var fiador = Primero(b, "fiador_nombre", "fiador");
        var tel = Primero(b, "tel_arrendatario", "telArrendatario");
        var telFiador = Primero(b, "fiador_telefono", "telFiador");
        var renta = b["renta"];

        if (!Js.T(nombre) || !Js.T(depto) || !Js.T(renta) || !Js.T(fechaInicio) || !Js.T(fechaTermino))
            throw new AppError("Nombre, departamento, renta y fechas son requeridos", 400);
        if (!await InquilinosRepository.ExisteDepartamento(u.Id, depto))
            throw new AppError($"Departamento {Js.Str(depto)} no existe o no te pertenece", 400);

        var creado = await InquilinosRepository.Insertar(u.Id, Guid.NewGuid().ToString(), new InquilinoDatos(
            Nombre: Js.TitleCase(Js.Str(nombre)), Depto: depto, Renta: renta,
            RentaLetra: Js.T(rentaLetra) ? Js.TitleCase(Js.Str(rentaLetra)) : null,
            Deposito: Js.T(b["deposito"]) ? b["deposito"] : 0,
            FechaPago: Primero(b, "fecha_pago", "fechaPago"), FechaInicio: fechaInicio, FechaTermino: fechaTermino,
            Fiador: Js.T(fiador) ? Js.TitleCase(Js.Str(fiador)) : null,
            Tel: Js.T(tel) ? tel : null, TelFiador: Js.T(telFiador) ? telFiador : null,
            Observaciones: Js.T(b["observaciones"]) ? b["observaciones"] : null,
            Inventario: (Js.T(b["inventario"]) ? b["inventario"]! : new JsonArray()).ToJsonString(),
            DepTipo: depTipo, DepFechas: depFechas.ToJsonString(), Metodo: metodo,
            Domicilio: Texto(b, "domicilio"), Identificacion: Texto(b, "identificacion"),
            EmailContacto: Texto(b, "email_contacto"), FiadorDomicilio: Texto(b, "fiador_domicilio")));

        await InquilinosRepository.SetEstadoDepartamento(u.Id, Js.Str(depto), "ocupado");
        await Audit.Log(u, ip, "inquilinos", "crear", creado.S("id")!, null, creado);
        return creado;
    }

    // ── Edición ──────────────────────────────────────────────────────────────
    public static async Task<Row> Actualizar(AuthUser u, string? ip, string id, JsonObject b)
    {
        var cur = await InquilinosRepository.Obtener(u.Id, id) ?? throw new AppError("Inquilino no encontrado", 404);
        JsonNode? Or(params string[] keys) => Primero(b, keys);
        // Un valor "sin contenido" en el cuerpo conserva el actual.
        object? Preferir(JsonNode? delCuerpo, object? actual) => Js.T(delCuerpo) ? delCuerpo : actual;

        object? fiador = b.ContainsKey("fiador_nombre")
            ? Js.TitleCase(Js.Str(Js.Or(b["fiador_nombre"], b["fiador"], JsonValue.Create(""))))
            : cur.S("fiador_nombre");
        object? observaciones = b.ContainsKey("observaciones")
            ? (Js.T(b["observaciones"]) ? Js.Str(b["observaciones"]) : "")
            : cur.S("observaciones");

        var actualizado = await InquilinosRepository.Actualizar(u.Id, id, new InquilinoDatos(
            Nombre: Js.TitleCase(Js.Str(Or("nombre_completo", "nombre")) ?? cur.S("nombre_completo")),
            Depto: Preferir(Or("depto_numero", "depto"), cur.S("depto_numero")),
            Renta: b["renta"] is null ? cur.S("renta") : b["renta"],
            RentaLetra: Js.TitleCase(Js.Str(Or("renta_letra", "rentaLetra")) is { Length: > 0 } rl ? rl : cur.S("renta_letra")),
            Deposito: b["deposito"] is null ? cur.S("deposito") : b["deposito"],
            FechaPago: Preferir(Or("fecha_pago", "fechaPago"), cur.S("fecha_pago")),
            FechaInicio: Preferir(Or("fecha_inicio", "fechaInicio"), cur.S("fecha_inicio")),
            FechaTermino: Preferir(Or("fecha_termino", "fechaTermino"), cur.S("fecha_termino")),
            Fiador: fiador,
            Tel: Preferir(Or("tel_arrendatario", "telArrendatario"), cur.S("tel_arrendatario")),
            TelFiador: Preferir(Or("fiador_telefono", "telFiador"), cur.S("fiador_telefono")),
            Observaciones: observaciones,
            Inventario: b.ContainsKey("inventario") ? (b["inventario"]?.ToJsonString() ?? "null") : cur.J("inventario")?.ToJsonString(),
            DepTipo: Preferir(Or("deposito_tipo", "depositoTipo"), string.IsNullOrEmpty(cur.S("deposito_tipo")) ? "ninguno" : cur.S("deposito_tipo")),
            DepFechas: (Js.T(Or("deposito_fechas", "depositoFechas")) ? Or("deposito_fechas", "depositoFechas")! : (cur.J("deposito_fechas") ?? new JsonArray())).ToJsonString(),
            Metodo: Preferir(Or("metodo_pago", "metodoPago"), string.IsNullOrEmpty(cur.S("metodo_pago")) ? "efectivo" : cur.S("metodo_pago")),
            Domicilio: TextoUpd(b, "domicilio", cur), Identificacion: TextoUpd(b, "identificacion", cur),
            EmailContacto: TextoUpd(b, "email_contacto", cur), FiadorDomicilio: TextoUpd(b, "fiador_domicilio", cur),
            Estado: b["estado"] is null ? cur.S("estado") : b["estado"]));

        // El departamento queda libre u ocupado según quién lo habite ahora.
        var deptoAnterior = cur.S("depto_numero"); var deptoNuevo = actualizado.S("depto_numero"); var estadoNuevo = actualizado.S("estado");
        if (deptoAnterior != deptoNuevo || cur.S("estado") != estadoNuevo)
        {
            if (!await InquilinosRepository.HayOtrosActivosEnDepto(u.Id, deptoAnterior, id))
                await InquilinosRepository.SetEstadoDepartamento(u.Id, deptoAnterior, "disponible");
            if (estadoNuevo == "activo")
                await InquilinosRepository.SetEstadoDepartamento(u.Id, deptoNuevo, "ocupado");
        }

        await Audit.Log(u, ip, "inquilinos", "editar", id, cur, actualizado);
        return actualizado;
    }

    // ── Baja y vinculación ───────────────────────────────────────────────────
    public static async Task DarDeBaja(AuthUser u, string? ip, string id)
    {
        var previo = await InquilinosRepository.Obtener(u.Id, id) ?? throw new AppError("Inquilino no encontrado", 404);
        await InquilinosRepository.DarDeBaja(u.Id, id);
        if (!await InquilinosRepository.HayOtrosActivosEnDepto(u.Id, previo.S("depto_numero"), id))
            await InquilinosRepository.SetEstadoDepartamento(u.Id, previo.S("depto_numero"), "disponible");
        await Audit.Log(u, ip, "inquilinos", "eliminar", id, previo, previo.With(("estado", "inactivo")));
    }

    public static async Task<Row?> Vincular(AuthUser u, string id, JsonNode? usuarioId)
    {
        if (!await InquilinosRepository.Existe(u.Id, id)) throw new AppError("Inquilino no encontrado", 404);
        var vincula = Js.T(usuarioId);
        // Solo se puede vincular una cuenta de inquilino que pertenezca a este propietario.
        if (vincula && !await InquilinosRepository.CuentaDeInquilinoDelPropietario(usuarioId, u.Id))
            throw new AppError("Usuario no encontrado o no autorizado", 404);
        return await InquilinosRepository.Vincular(u.Id, id, vincula ? usuarioId : null);
    }

    // ── PDF del contrato ─────────────────────────────────────────────────────
    /// <summary>Token de 2 min, solo para el PDF de ESTE inquilino (el navegador abre el enlace sin cabeceras).</summary>
    public static async Task<string> GenerarTokenPdf(AuthUser u, string id)
    {
        if (!await InquilinosRepository.Existe(u.Id, id)) throw new AppError("Inquilino no encontrado", 404);
        return Jwt.Create(new JsonObject { ["scope"] = "pdf_download", ["inquilino_id"] = id, ["admin_id"] = u.Id }, "2m");
    }

    /// <summary>El último contrato generado o, si no hay, uno nuevo al vuelo con la plantilla que le toca.</summary>
    public static async Task<(byte[] Pdf, string Nombre)> PdfDelContrato(string adminId, string id)
    {
        var ctx = await ContratoServicio.CargarContexto(adminId, id);
        var pdf = await InquilinosRepository.UltimoPdfGenerado(adminId, id)
            ?? (await ContratoServicio.Generar(await ContratoServicio.ResolverPlantilla(adminId, ctx.Inquilino, ctx.Depto), ctx, null)).Pdf;
        var nombre = Regex.Replace(ctx.Inquilino.S("nombre_completo") ?? "", @"\s+", "_");
        return (pdf, $"contrato_{ctx.Inquilino.S("depto_numero")}_{nombre}.pdf");
    }
}
