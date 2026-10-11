using System.Text;
using System.Text.Json.Nodes;

namespace VertexRent.Api;

public static class InquilinosEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/inquilinos");

        // El PDF lo abre el navegador directamente (sin headers): token de un solo propósito (ver PdfAuth).
        g.MapGet("/{id}/pdf", H(GetContratoPdf)).PdfAuth();

        var a = g.MapGroup("").Auth();

        a.MapPost("/{id}/pdf-token", H(GenerarTokenPdf)).Permiso("contratos");

        a.MapGet("/mi-depto", H(GetMiDepto));
        a.MapPost("/extraer-ine", H(ExtraerIne)).AdminOnly();

        a.MapGet("/", H(GetInquilinos)).Permiso("inquilinos");
        a.MapGet("/{id}", H(GetById)).Permiso("inquilinos");
        a.MapPost("/", H(Create)).Permiso("inquilinos.editar");
        a.MapPut("/{id}", H(Update)).Permiso("inquilinos.editar");
        a.MapPut("/{id}/vincular-usuario", H(VincularUsuario)).AdminOnly();
        a.MapDelete("/{id}", H(Delete)).Permiso("inquilinos.editar");
    }

    // Texto opcional del cuerpo: vacío → null.
    static string? Texto(JsonObject b, string k) => Js.Str(b[k])?.Trim() is { Length: > 0 } t ? (t.Length > 500 ? t[..500] : t) : null;
    // En una edición, si el campo no viene se conserva el valor actual; si viene vacío se borra.
    static string? TextoUpd(JsonObject b, string k, Row cur) => b.ContainsKey(k) ? Texto(b, k) : cur.S(k);

    static async Task<IResult> GetMiDepto(HttpContext c)
    {
        var r = await Db.Query(
            "SELECT i.* FROM inquilinos i WHERE i.usuario_id = $1 AND i.estado = 'activo' LIMIT 1", c.Me().Id);
        return Api.Data(r.First);
    }

    static async Task<IResult> GetInquilinos(HttpContext c)
    {
        var search = Api.Query(c, "search"); var estado = Api.Query(c, "estado"); var depto = Api.Query(c, "depto");

        var sql = new StringBuilder(@"
            SELECT i.*,
              (SELECT json_build_object(
                'id', u.id, 'nombre', u.nombre_completo, 'email', u.email
              ) FROM usuarios u WHERE u.id = i.usuario_id) as usuario,
              (SELECT json_build_object(
                'ultimo_accion', a.accion,
                'ultimo_usuario', (SELECT nombre_completo FROM usuarios WHERE id = a.usuario_id),
                'ultima_fecha', a.created_at
              ) FROM auditoria a WHERE a.registro_id = i.id ORDER BY a.created_at DESC LIMIT 1) as auditoria
            FROM inquilinos i
            WHERE i.admin_id = $1");
        var ps = new List<object?> { c.Me().Id };
        var idx = 2;

        if (search != null)
        {
            sql.Append($@" AND (
                i.nombre_completo ILIKE ${idx} OR
                i.fiador_nombre ILIKE ${idx} OR
                i.tel_arrendatario ILIKE ${idx} OR
                i.depto_numero::text = ${idx + 1}
            )");
            ps.Add($"%{search}%"); ps.Add(search);
            idx += 2;
        }
        if (estado != null) { sql.Append($" AND i.estado = ${idx++}"); ps.Add(estado); }
        if (depto != null) { sql.Append($" AND i.depto_numero = ${idx++}"); ps.Add(depto); }
        sql.Append(" ORDER BY i.depto_numero ASC");

        var r = await Db.Query(sql.ToString(), ps.ToArray());
        return Api.Obj(200, ("data", r.Rows), ("total", r.RowCount));
    }

    static async Task<IResult> GetById(HttpContext c)
    {
        var r = await Db.Query(
            @"SELECT i.*,
                (SELECT json_agg(json_build_object(
                  'id', a.id,
                  'accion', a.accion,
                  'usuario', (SELECT nombre_completo FROM usuarios WHERE id = a.usuario_id),
                  'datos_anteriores', a.datos_anteriores,
                  'datos_nuevos', a.datos_nuevos,
                  'fecha', a.created_at
                ) ORDER BY a.created_at DESC)
                FROM auditoria a WHERE a.registro_id = i.id) as historial
              FROM inquilinos i WHERE i.admin_id = $1 AND i.id = $2", c.Me().Id, Api.Route(c, "id"));
        if (r.First == null) throw new AppError("Inquilino no encontrado", 404);
        return Api.Data(r.First);
    }

    static async Task<IResult> Create(HttpContext c)
    {
        var b = await Api.Body(c);
        JsonNode? Pick(params string[] keys) => Js.Or(keys.Select(k => b[k]).ToArray());

        var depTipo = Js.T(Pick("deposito_tipo", "depositoTipo")) ? Pick("deposito_tipo", "depositoTipo") : JsonValue.Create("ninguno");
        var depFechas = Js.T(Pick("deposito_fechas", "depositoFechas")) ? Pick("deposito_fechas", "depositoFechas")! : new JsonArray();
        var metodo = Js.T(Pick("metodo_pago", "metodoPago")) ? Pick("metodo_pago", "metodoPago") : JsonValue.Create("efectivo");

        var nombre = Pick("nombre_completo", "nombre");
        var depto = Pick("depto_numero", "depto");
        var rentaLetra = Pick("renta_letra", "rentaLetra");
        var fechaPago = Pick("fecha_pago", "fechaPago");
        var fechaInicio = Pick("fecha_inicio", "fechaInicio");
        var fechaTermino = Pick("fecha_termino", "fechaTermino");
        var fiador = Pick("fiador_nombre", "fiador");
        var tel = Pick("tel_arrendatario", "telArrendatario");
        var telFiador = Pick("fiador_telefono", "telFiador");
        var renta = b["renta"];

        if (!Js.T(nombre) || !Js.T(depto) || !Js.T(renta) || !Js.T(fechaInicio) || !Js.T(fechaTermino))
            throw new AppError("Nombre, departamento, renta y fechas son requeridos", 400);

        var u = c.Me();
        var deptoCheck = await Db.Query("SELECT numero FROM departamentos WHERE admin_id = $1 AND numero = $2", u.Id, depto);
        if (deptoCheck.First == null) throw new AppError($"Departamento {Js.Str(depto)} no existe o no te pertenece", 400);

        var r = await Db.Query(
            @"INSERT INTO inquilinos (
                admin_id, invitation_token,
                nombre_completo, depto_numero, renta, renta_letra, deposito, fecha_pago,
                fecha_inicio, fecha_termino, fiador_nombre, tel_arrendatario,
                fiador_telefono, observaciones, inventario, deposito_tipo, deposito_fechas,
                metodo_pago, domicilio, identificacion, email_contacto, fiador_domicilio
              ) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,$22)
              RETURNING *",
            u.Id, Guid.NewGuid().ToString(),
            Js.TitleCase(Js.Str(nombre)), depto, renta,
            Js.T(rentaLetra) ? Js.TitleCase(Js.Str(rentaLetra)) : null,
            Js.T(b["deposito"]) ? b["deposito"] : 0,
            fechaPago, fechaInicio, fechaTermino,
            Js.T(fiador) ? Js.TitleCase(Js.Str(fiador)) : null,
            Js.T(tel) ? tel : null, Js.T(telFiador) ? telFiador : null,
            Js.T(b["observaciones"]) ? b["observaciones"] : null,
            (Js.T(b["inventario"]) ? b["inventario"]! : new JsonArray()).ToJsonString(),
            depTipo, depFechas.ToJsonString(), metodo,
            Texto(b, "domicilio"), Texto(b, "identificacion"), Texto(b, "email_contacto"), Texto(b, "fiador_domicilio"));
        var created = r.First!;

        await Db.Query("UPDATE departamentos SET estado = 'ocupado' WHERE admin_id = $1 AND numero = $2", u.Id, depto);
        await Audit.Log(c, "inquilinos", "crear", created.S("id")!, null, created);
        return Api.Data(created, 201);
    }

    static async Task<IResult> Update(HttpContext c)
    {
        var id = Api.Route(c, "id");
        var b = await Api.Body(c);
        var u = c.Me();

        var anterior = await Db.Query("SELECT * FROM inquilinos WHERE admin_id = $1 AND id = $2", u.Id, id);
        var cur = anterior.First ?? throw new AppError("Inquilino no encontrado", 404);
        JsonNode? Or(params string[] keys) => Js.Or(keys.Select(k => b[k]).ToArray());
        object? Prefer(JsonNode? fromBody, object? fallback) => Js.T(fromBody) ? fromBody : fallback;

        object? fiador = b.ContainsKey("fiador_nombre")
            ? Js.TitleCase(Js.Str(Js.Or(b["fiador_nombre"], b["fiador"], JsonValue.Create(""))))
            : cur.S("fiador_nombre");
        object? obs = b.ContainsKey("observaciones")
            ? (Js.T(b["observaciones"]) ? Js.Str(b["observaciones"]) : "")
            : cur.S("observaciones");

        var r = await Db.Query(
            @"UPDATE inquilinos SET
                nombre_completo   = $1,
                depto_numero      = $2,
                renta             = $3,
                renta_letra       = $4,
                deposito          = $5,
                fecha_pago        = $6,
                fecha_inicio      = $7,
                fecha_termino     = $8,
                fiador_nombre     = $9,
                tel_arrendatario  = $10,
                fiador_telefono   = $11,
                observaciones     = $12,
                inventario        = $13,
                estado            = $14,
                deposito_tipo     = $15,
                deposito_fechas   = $16,
                metodo_pago       = $17,
                domicilio         = $20,
                identificacion    = $21,
                email_contacto    = $22,
                fiador_domicilio  = $23
              WHERE admin_id = $18 AND id = $19 RETURNING *",
            Js.TitleCase(Js.Str(Or("nombre_completo", "nombre")) ?? cur.S("nombre_completo")),
            Prefer(Or("depto_numero", "depto"), cur.S("depto_numero")),
            b["renta"] is null ? cur.S("renta") : b["renta"],
            Js.TitleCase(Js.Str(Or("renta_letra", "rentaLetra")) is { Length: > 0 } rl ? rl : cur.S("renta_letra")),
            b["deposito"] is null ? cur.S("deposito") : b["deposito"],
            Prefer(Or("fecha_pago", "fechaPago"), cur.S("fecha_pago")),
            Prefer(Or("fecha_inicio", "fechaInicio"), cur.S("fecha_inicio")),
            Prefer(Or("fecha_termino", "fechaTermino"), cur.S("fecha_termino")),
            fiador,
            Prefer(Or("tel_arrendatario", "telArrendatario"), cur.S("tel_arrendatario")),
            Prefer(Or("fiador_telefono", "telFiador"), cur.S("fiador_telefono")),
            obs,
            b.ContainsKey("inventario") ? (b["inventario"]?.ToJsonString() ?? "null") : cur.J("inventario")?.ToJsonString(),
            b["estado"] is null ? cur.S("estado") : b["estado"],
            Prefer(Or("deposito_tipo", "depositoTipo"), string.IsNullOrEmpty(cur.S("deposito_tipo")) ? "ninguno" : cur.S("deposito_tipo")),
            (Js.T(Or("deposito_fechas", "depositoFechas")) ? Or("deposito_fechas", "depositoFechas")! : (cur.J("deposito_fechas") ?? new JsonArray())).ToJsonString(),
            Prefer(Or("metodo_pago", "metodoPago"), string.IsNullOrEmpty(cur.S("metodo_pago")) ? "efectivo" : cur.S("metodo_pago")),
            u.Id, id,
            TextoUpd(b, "domicilio", cur), TextoUpd(b, "identificacion", cur), TextoUpd(b, "email_contacto", cur), TextoUpd(b, "fiador_domicilio", cur));
        var updated = r.First!;

        // Sincronizar estado de departamentos si cambió el depto asignado o el estado del inquilino
        var deptoAnterior = cur.S("depto_numero"); var deptoNuevo = updated.S("depto_numero");
        var estadoNuevo = updated.S("estado");
        if (deptoAnterior != deptoNuevo || cur.S("estado") != estadoNuevo)
        {
            var otros = await Db.Query(
                "SELECT id FROM inquilinos WHERE admin_id = $1 AND depto_numero = $2 AND estado = 'activo' AND id != $3",
                u.Id, deptoAnterior, id);
            if (otros.Rows.Count == 0)
                await Db.Query("UPDATE departamentos SET estado = 'disponible' WHERE admin_id = $1 AND numero = $2", u.Id, deptoAnterior);
            if (estadoNuevo == "activo")
                await Db.Query("UPDATE departamentos SET estado = 'ocupado' WHERE admin_id = $1 AND numero = $2", u.Id, deptoNuevo);
        }

        await Audit.Log(c, "inquilinos", "editar", id, cur, updated);
        return Api.Data(updated);
    }

    static async Task<IResult> VincularUsuario(HttpContext c)
    {
        var id = Api.Route(c, "id");
        var b = await Api.Body(c);
        var u = c.Me();
        var check = await Db.Query("SELECT id FROM inquilinos WHERE admin_id = $1 AND id = $2", u.Id, id);
        if (check.First == null) throw new AppError("Inquilino no encontrado", 404);
        var r = await Db.Query(
            "UPDATE inquilinos SET usuario_id = $1 WHERE admin_id = $2 AND id = $3 RETURNING id, nombre_completo, usuario_id",
            Js.T(b["usuario_id"]) ? b["usuario_id"] : null, u.Id, id);
        return Api.Data(r.First);
    }

    static async Task<IResult> Delete(HttpContext c)
    {
        var id = Api.Route(c, "id");
        var u = c.Me();
        var anterior = await Db.Query("SELECT * FROM inquilinos WHERE admin_id = $1 AND id = $2", u.Id, id);
        var prev = anterior.First ?? throw new AppError("Inquilino no encontrado", 404);

        // Soft delete: conservar historial, solo marcar como inactivo
        await Db.Query("UPDATE inquilinos SET estado = 'inactivo' WHERE admin_id = $1 AND id = $2", u.Id, id);

        var otros = await Db.Query(
            "SELECT id FROM inquilinos WHERE admin_id = $1 AND depto_numero = $2 AND estado = 'activo' AND id != $3",
            u.Id, prev.S("depto_numero"), id);
        if (otros.Rows.Count == 0)
            await Db.Query("UPDATE departamentos SET estado = 'disponible' WHERE admin_id = $1 AND numero = $2", u.Id, prev.S("depto_numero"));

        await Audit.Log(c, "inquilinos", "eliminar", id, prev, prev.With(("estado", "inactivo")));
        return Api.Msg("Inquilino dado de baja correctamente");
    }

    // ── INE con Gemini ───────────────────────────────────────────────────────
    static async Task<IResult> ExtraerIne(HttpContext c)
    {
        var b = await Api.Body(c);
        var img = b.S("imagen_base64");
        if (string.IsNullOrEmpty(img)) throw new AppError("imagen_base64 es requerida", 400);
        var apiKey = GeminiService.RequireKey("GOOGLE_AI_API_KEY no configurada en el servidor");

        // "data:image/jpeg;base64,<data>"
        var comma = img.IndexOf(',');
        var header = comma > -1 ? img[..comma] : "";
        var puro = comma > -1 ? img[(comma + 1)..] : img;
        var mm = System.Text.RegularExpressions.Regex.Match(header, @":(.*?);");
        var mime = mm.Success ? mm.Groups[1].Value : "image/jpeg";

        var raw = (await GeminiService.Generate(apiKey,
            "Eres un extractor de datos de documentos de identidad mexicanos (INE/credencial de elector). " +
            "Analiza la imagen y extrae los campos indicados. " +
            "IMPORTANTE: responde SOLO con el objeto JSON, sin texto adicional, sin bloques de código, sin explicaciones.\n" +
            "Formato exacto requerido:\n" +
            "{\"nombre_completo\":\"\",\"domicilio\":\"\",\"colonia\":\"\",\"municipio\":\"\",\"estado\":\"\",\"cp\":\"\"}\n" +
            "Si un campo no es legible déjalo como \"\".", (mime, puro))).Trim();
        Console.WriteLine($"[extraerIne] respuesta del modelo: {raw[..Math.Min(300, raw.Length)]}");

        JsonNode? datos;
        try
        {
            var first = raw.IndexOf('{'); var last = raw.LastIndexOf('}');
            if (first == -1 || last == -1) throw new Exception("no braces");
            datos = JsonNode.Parse(raw[first..(last + 1)]);
        }
        catch
        {
            Console.Error.WriteLine($"[extraerIne] texto recibido: {raw}");
            throw new AppError("El modelo no devolvió JSON válido. Intenta con una imagen más clara o mejor iluminación.", 422);
        }
        return Api.Data(datos);
    }

    // ── Contrato: PDF y editor ───────────────────────────────────────────────
    static async Task<IResult> GenerarTokenPdf(HttpContext c)
    {
        var id = Api.Route(c, "id");
        var u = c.Me();
        var check = await Db.Query("SELECT id FROM inquilinos WHERE admin_id = $1 AND id = $2", u.Id, id);
        if (check.First == null) throw new AppError("Inquilino no encontrado", 404);

        // Token de corta duración, scope limitado, solo para el PDF de ESTE inquilino.
        var token = Jwt.Create(new JsonObject { ["scope"] = "pdf_download", ["inquilino_id"] = id, ["admin_id"] = u.Id }, "2m");
        return Api.Data(new Dictionary<string, object?> { ["token"] = token });
    }

    internal static IResult PdfResponse(HttpContext c, byte[] pdf, string filename)
    {
        var ascii = new string(filename.Normalize(NormalizationForm.FormD)
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(ch => ch < 128 && ch != '"' && ch != '\\' ? ch : '_').ToArray());
        c.Response.Headers.ContentDisposition = $"inline; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(filename)}";
        return Results.File(pdf, "application/pdf");
    }

    // Compatibilidad: devuelve el último contrato generado o, si no hay, genera uno al vuelo con la plantilla que le toca.
    static async Task<IResult> GetContratoPdf(HttpContext c)
    {
        var adminId = c.Me().Id; var id = Api.Route(c, "id");
        var ctx = await ContratoServicio.CargarContexto(adminId, id);
        var ultimo = await Db.Query("SELECT pdf FROM contratos WHERE admin_id = $1 AND inquilino_id = $2 AND pdf IS NOT NULL ORDER BY version DESC LIMIT 1", adminId, id);
        var pdf = ultimo.First != null
            ? (byte[])ultimo.First["pdf"]!
            : (await ContratoServicio.Generar(await ContratoServicio.ResolverPlantilla(adminId, ctx.Inquilino, ctx.Depto), ctx, null)).Pdf;
        var nombre = System.Text.RegularExpressions.Regex.Replace(ctx.Inquilino.S("nombre_completo") ?? "", @"\s+", "_");
        return PdfResponse(c, pdf, $"contrato_{ctx.Inquilino.S("depto_numero")}_{nombre}.pdf");
    }
}
