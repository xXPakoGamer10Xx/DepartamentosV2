using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VertexRent.Api;

public static class ContratosEndpoints
{
    // Datos que se pueden completar al generar un contrato: variable → (destino, columna)
    static readonly Dictionary<string, (string Destino, string Campo, string Etiqueta)> Completables = new()
    {
        ["inquilino_domicilio"] = ("inquilino", "domicilio", "Domicilio del inquilino"),
        ["inquilino_identificacion"] = ("inquilino", "identificacion", "Identificación del inquilino"),
        ["inquilino_email"] = ("inquilino", "email_contacto", "Correo del inquilino"),
        ["tel_arrendatario"] = ("inquilino", "tel_arrendatario", "Teléfono del inquilino"),
        ["fiador_domicilio"] = ("inquilino", "fiador_domicilio", "Domicilio del fiador"),
        ["propiedad_direccion"] = ("propiedad", "ubicacion", "Dirección de la propiedad"),
        ["propiedad_descripcion"] = ("propiedad", "descripcion", "Descripción de la propiedad"),
        ["arrendador_nombre"] = ("config", "arrendador_nombre", "Nombre del arrendador"),
        ["arrendador_direccion"] = ("config", "arrendador_direccion", "Dirección del arrendador"),
        ["arrendador_rfc"] = ("config", "arrendador_rfc", "RFC del arrendador"),
        ["arrendador_telefono"] = ("config", "arrendador_telefono", "Teléfono del arrendador"),
    };

    static readonly Regex ClaveCampo = new(@"^campo_[a-z0-9_]{1,40}$", RegexOptions.Compiled);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/contratos").Auth();

        g.MapGet("/variables", H(Variables)).Permiso("contratos");
        g.MapGet("/plantillas", H(ListarPlantillas)).Permiso("contratos");
        g.MapPost("/plantillas", H(SubirPlantilla)).AdminOnly();
        g.MapGet("/plantillas/{id}", H(DetallePlantilla)).Permiso("contratos");
        g.MapPut("/plantillas/{id}", H(GuardarPlantilla)).AdminOnly();
        g.MapDelete("/plantillas/{id}", H(EliminarPlantilla)).AdminOnly();
        g.MapPost("/plantillas/{id}/preview", H(PreviewPlantilla)).Permiso("contratos");
        g.MapGet("/plantillas/{id}/docx", H(DescargarPlantilla)).Permiso("contratos");
        g.MapPost("/plantillas/{id}/predeterminada", H(Predeterminada)).AdminOnly();
        g.MapPut("/plantillas/{id}/asignar", H(Asignar)).AdminOnly();

        g.MapGet("/inquilino/{id}/preparar", H(Preparar)).Permiso("contratos");
        g.MapPost("/inquilino/{id}/generar", H(GenerarContrato)).Permiso("contratos");
        g.MapPost("/generar-pendientes", H(GenerarPendientes)).Permiso("contratos");

        g.MapGet("/resumen", H(Resumen)).Permiso("contratos");
        g.MapGet("/", H(Historial)).Permiso("contratos");
        g.MapGet("/{id}/archivo", H(Archivo)).Permiso("contratos");
        g.MapDelete("/{id}", H(EliminarContrato)).Permiso("contratos");
    }

    // ── Catálogo ─────────────────────────────────────────────────────────────────────────────
    static Task<IResult> Variables(HttpContext c) => Task.FromResult(Api.Data(new Dictionary<string, object?>
    {
        ["variables"] = ContratoVariables.Todas.Select(v => new Dictionary<string, object?>
        {
            ["key"] = v.Key, ["etiqueta"] = v.Etiqueta, ["grupo"] = v.Grupo, ["ejemplo"] = v.Ejemplo,
        }).ToList(),
        ["motor"] = LibreOfficeService.Disponible ? "libreoffice" : "aproximado",
        ["ia"] = ContratoAnalisisService.Disponible,
    }));

    // ── Plantillas ───────────────────────────────────────────────────────────────────────────
    static async Task<IResult> ListarPlantillas(HttpContext c)
    {
        var u = c.Me();
        var r = await Db.Query(
            @"SELECT p.id, p.nombre, p.descripcion, p.estado, p.predeterminada, p.campos, p.created_at, p.updated_at,
                     (SELECT COUNT(*)::int FROM departamentos d WHERE d.plantilla_id = p.id) AS propiedades,
                     (SELECT COUNT(*)::int FROM inquilinos i WHERE i.plantilla_id = p.id) AS inquilinos
              FROM plantillas_contrato p WHERE p.admin_id = $1 ORDER BY p.predeterminada DESC, p.created_at DESC", u.Id);
        var hayPropia = r.Rows.Any(x => x.B("predeterminada"));
        var lista = new List<object?>
        {
            new Dictionary<string, object?>
            {
                ["id"] = PlantillaSistema.Id, ["nombre"] = PlantillaSistema.Nombre, ["descripcion"] = PlantillaSistema.Descripcion,
                ["estado"] = "lista", ["sistema"] = true, ["predeterminada"] = !hayPropia, ["campos"] = new JsonArray(),
                ["propiedades"] = 0, ["inquilinos"] = 0,
            },
        };
        lista.AddRange(r.Rows.Select(x => (object?)x.With(("sistema", false))));
        return Api.Data(lista);
    }

    static byte[] DecodificarDocx(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) throw new AppError("Sube un archivo Word (.docx) o PDF", 400);
        var coma = base64.IndexOf(',');
        if (base64.StartsWith("data:") && coma > -1) base64 = base64[(coma + 1)..];
        if (base64.Length > DocxService.MaxBytes * 4 / 3 + 1024) throw new AppError("El archivo excede el límite de 5 MB", 400);
        try { return Convert.FromBase64String(base64); }
        catch (FormatException) { throw new AppError("El archivo no es válido", 400); }
    }

    static string LimpiarNombre(string? n, string porDefecto)
    {
        var t = Regex.Replace(n ?? "", @"\s+", " ").Trim();
        if (t.Length == 0) t = porDefecto;
        return t.Length > 120 ? t[..120] : t;
    }

    // Sube el Word, lo valida y propone dónde van los datos (IA). Queda como borrador hasta guardarlo.
    static async Task<IResult> SubirPlantilla(HttpContext c)
    {
        var u = c.Me(); var b = await Api.Body(c);
        var bytes = DecodificarDocx(b.S("archivoBase64") ?? b.S("docxBase64"));
        // Un PDF se convierte primero a Word (con IA); desde ahí el flujo es el mismo que con un .docx.
        string? avisoPdf = null; var desdePdf = PdfImportService.EsPdf(bytes);
        if (desdePdf)
        {
            var conv = await PdfImportService.ConvertirADocx(bytes);
            bytes = conv.Docx; avisoPdf = conv.Aviso;
        }
        var original = DocxService.Sanear(Validado(bytes));
        var nombre = LimpiarNombre(b.S("nombre"), "Mi contrato");

        var presentes = DocxService.VariablesPresentes(original);
        var sugerencias = new List<Sugerencia>(); string? iaError = null;
        // Un Word que ya trae sus {{variables}} no necesita IA.
        if (presentes.Count < 3)
        {
            if (!ContratoAnalisisService.Disponible) iaError = "La IA no está configurada: marca los datos a mano.";
            else
                try { sugerencias = await ContratoAnalisisService.Sugerir(original); }
                catch (Exception ex) { iaError = "No se pudo analizar con IA: " + (ex is AppError ae ? ae.Message : "intenta de nuevo"); Console.Error.WriteLine($"Análisis IA: {ex.Message}"); }
        }

        var r = await Db.Query(
            @"INSERT INTO plantillas_contrato (admin_id, nombre, docx_original, docx_plantilla, estado)
              VALUES ($1, $2, $3, $3, 'borrador') RETURNING id", u.Id, nombre, original);
        var id = r.First!.S("id")!;
        await Audit.Log(c, "plantillas_contrato", "crear", id, null, new Dictionary<string, object?> { ["nombre"] = nombre });

        return Api.Data(new Dictionary<string, object?>
        {
            ["id"] = id, ["nombre"] = nombre,
            ["html"] = DocxService.HtmlDeRevision(original, Array.Empty<Marca>()),
            ["sugerencias"] = sugerencias.Select(s => s.ToJson()).ToList(),
            ["variables_presentes"] = presentes,
            ["ia_error"] = iaError,
            ["desde_pdf"] = desdePdf,
            ["aviso_conversion"] = avisoPdf,
        }, 201);
    }

    static byte[] Validado(byte[] docx) { DocxService.Validar(docx); return docx; }

    static JsonObject MarcaJson(Marca m, string texto, JsonArray campos)
    {
        var campo = campos.FirstOrDefault(x => Js.Str(x?["key"]) == m.Key);
        return new JsonObject
        {
            ["p"] = m.P, ["ini"] = m.Ini, ["fin"] = m.Fin, ["texto"] = texto, ["key"] = m.Key, ["campo"] = campo?.DeepClone(),
            ["borrar"] = m.Borrar == null ? null : new JsonArray(m.Borrar.Select(b => (JsonNode)b!).ToArray()),
        };
    }

    static List<Marca> LeerMarcas(JsonNode? n) =>
        (n as JsonArray)?.Select(x => new Marca(Js.Str(x?["p"]) ?? "", (int)(Js.ParseFloat(x?["ini"])), (int)(Js.ParseFloat(x?["fin"])), Js.Str(x?["key"]) ?? "",
            (x?["borrar"] as JsonArray)?.Select(b => Js.Str(b) ?? "").Where(b => b.Length > 0).ToArray())).ToList() ?? new();

    static async Task<IResult> DetallePlantilla(HttpContext c)
    {
        var u = c.Me(); var id = Api.Route(c, "id");
        if (id == PlantillaSistema.Id)
            return Api.Data(new Dictionary<string, object?>
            {
                ["id"] = PlantillaSistema.Id, ["nombre"] = PlantillaSistema.Nombre, ["sistema"] = true, ["estado"] = "lista",
                ["campos"] = new JsonArray(), ["marcas"] = new JsonArray(),
                ["html"] = DocxService.HtmlDeRevision(PlantillaSistema.Docx, Array.Empty<Marca>()),
            });
        if (!Guid.TryParse(id, out _)) throw new AppError("Plantilla no encontrada", 404);
        var r = await Db.Query(
            "SELECT id, nombre, estado, predeterminada, campos, marcas, docx_original FROM plantillas_contrato WHERE admin_id = $1 AND id = $2", u.Id, id);
        var p = r.First ?? throw new AppError("Plantilla no encontrada", 404);
        var original = (byte[])p["docx_original"]!;
        var campos = p.J("campos") as JsonArray ?? new JsonArray();
        var marcas = LeerMarcas(p.J("marcas"));
        var textos = DocxService.ExtraerParrafos(original).ToDictionary(x => x.Id, x => x.Texto);
        return Api.Data(new Dictionary<string, object?>
        {
            ["id"] = id, ["nombre"] = p.S("nombre"), ["sistema"] = false, ["estado"] = p.S("estado"), ["predeterminada"] = p.B("predeterminada"),
            ["campos"] = campos,
            ["marcas"] = new JsonArray(marcas.Select(m => (JsonNode)MarcaJson(m, textos.TryGetValue(m.P, out var t) && m.Fin <= t.Length ? t[m.Ini..m.Fin] : "", campos)).ToArray()),
            ["html"] = DocxService.HtmlDeRevision(original, Array.Empty<Marca>()),
            ["variables_presentes"] = DocxService.VariablesPresentes(original),
        });
    }

    static async Task<IResult> GuardarPlantilla(HttpContext c)
    {
        var u = c.Me(); var id = Api.Route(c, "id"); var b = await Api.Body(c);
        if (id == PlantillaSistema.Id) throw new AppError("La plantilla del sistema no se puede modificar. Descárgala y sube tu versión.", 400);
        if (!Guid.TryParse(id, out _)) throw new AppError("Plantilla no encontrada", 404);
        var r = await Db.Query("SELECT nombre, estado, campos, docx_original FROM plantillas_contrato WHERE admin_id = $1 AND id = $2", u.Id, id);
        var cur = r.First ?? throw new AppError("Plantilla no encontrada", 404);
        var original = (byte[])cur["docx_original"]!;

        // Campos personalizados (definiciones)
        var campos = new JsonArray();
        var clavesCampo = new HashSet<string>();
        foreach (var x in (b["campos"] as JsonArray ?? (cur.J("campos") as JsonArray) ?? new JsonArray()))
        {
            var key = Js.Str(x?["key"]) ?? "";
            if (!ClaveCampo.IsMatch(key) || !clavesCampo.Add(key)) continue;
            var tipo = Js.Str(x?["tipo"]); if (tipo is not ("texto" or "numero" or "fecha")) tipo = "texto";
            campos.Add(new JsonObject
            {
                ["key"] = key, ["etiqueta"] = LimpiarNombre(Js.Str(x?["etiqueta"]), key), ["tipo"] = tipo,
                ["requerido"] = x?["requerido"]?.GetValue<bool>() ?? false,
            });
        }

        // Marcas: se validan contra el texto real del documento original.
        var textos = DocxService.ExtraerParrafos(original).ToDictionary(x => x.Id, x => x.Texto);
        var marcas = b.ContainsKey("marcas") ? LeerMarcas(b["marcas"]) : null;
        JsonArray? marcasJson = null; byte[]? plantilla = null;
        if (marcas != null)
        {
            foreach (var m in marcas)
            {
                if (!textos.TryGetValue(m.P, out var t) || m.Ini < 0 || m.Fin > t.Length || m.Fin <= m.Ini)
                    throw new AppError("Una de las marcas no coincide con el documento. Recarga e inténtalo de nuevo.", 400);
                if (!ContratoVariables.Claves.Contains(m.Key) && !clavesCampo.Contains(m.Key))
                    throw new AppError($"La variable \"{m.Key}\" no existe", 400);
                if (Regex.Matches(t, @"\{\{[^}]*\}\}").Any(x => m.Ini < x.Index + x.Length && m.Fin > x.Index))
                    throw new AppError("No se puede marcar texto que ya es una variable", 400);
            }
            foreach (var grp in marcas.GroupBy(m => m.P))
            {
                var orden = grp.OrderBy(m => m.Ini).ToList();
                for (var i = 1; i < orden.Count; i++)
                    if (orden[i].Ini < orden[i - 1].Fin) throw new AppError("Hay dos marcas que se encimarían en el mismo texto", 400);
            }
            plantilla = DocxService.AplicarMarcas(original, marcas);
            marcasJson = new JsonArray(marcas.Select(m => (JsonNode)MarcaJson(m, textos[m.P][m.Ini..m.Fin], campos)).ToArray());
        }

        var estado = b.S("estado") is "lista" or "borrador" ? b.S("estado")! : cur.S("estado")!;
        var nombre = LimpiarNombre(b.S("nombre"), cur.S("nombre")!);
        var predeterminada = b.ContainsKey("predeterminada") ? b["predeterminada"]?.GetValue<bool>() ?? false : (bool?)null;
        if (predeterminada == true && estado != "lista") throw new AppError("Solo una plantilla lista puede ser la predeterminada", 400);

        await Db.Tx(async tx =>
        {
            if (predeterminada == true)
                await tx.Query("UPDATE plantillas_contrato SET predeterminada = FALSE WHERE admin_id = $1 AND id <> $2", u.Id, id);
            await tx.Query(
                @"UPDATE plantillas_contrato SET nombre = $3, estado = $4, campos = $5,
                    marcas = COALESCE($6::jsonb, marcas), docx_plantilla = COALESCE($7, docx_plantilla),
                    predeterminada = COALESCE($8::boolean, predeterminada), updated_at = NOW()
                  WHERE admin_id = $1 AND id = $2",
                u.Id, id, nombre, estado, campos.ToJsonString(), marcasJson?.ToJsonString(), plantilla, predeterminada?.ToString().ToLowerInvariant());
            return true;
        });
        await Audit.Log(c, "plantillas_contrato", "editar", id, new Dictionary<string, object?> { ["nombre"] = cur.S("nombre") },
            new Dictionary<string, object?> { ["nombre"] = nombre, ["estado"] = estado });
        return Api.Data(new Dictionary<string, object?> { ["id"] = id, ["nombre"] = nombre, ["estado"] = estado });
    }

    static async Task<IResult> EliminarPlantilla(HttpContext c)
    {
        var u = c.Me(); var id = Api.Route(c, "id");
        if (id == PlantillaSistema.Id) throw new AppError("La plantilla del sistema no se puede eliminar", 400);
        if (!Guid.TryParse(id, out _)) throw new AppError("Plantilla no encontrada", 404);
        var r = await Db.Query("DELETE FROM plantillas_contrato WHERE admin_id = $1 AND id = $2 RETURNING id, nombre", u.Id, id);
        if (r.First == null) throw new AppError("Plantilla no encontrada", 404);
        await Audit.Log(c, "plantillas_contrato", "eliminar", id, r.First, null);
        return Api.Msg("Plantilla eliminada");
    }

    static async Task<IResult> Predeterminada(HttpContext c)
    {
        var u = c.Me(); var id = Api.Route(c, "id");
        await Db.Tx(async tx =>
        {
            await tx.Query("UPDATE plantillas_contrato SET predeterminada = FALSE WHERE admin_id = $1", u.Id);
            if (id != PlantillaSistema.Id)
            {
                if (!Guid.TryParse(id, out _)) throw new AppError("Plantilla no encontrada", 404);
                var r = await tx.Query("UPDATE plantillas_contrato SET predeterminada = TRUE WHERE admin_id = $1 AND id = $2 AND estado = 'lista' RETURNING id", u.Id, id);
                if (r.First == null) throw new AppError("La plantilla no existe o aún no está lista", 400);
            }
            return true;
        });
        return Api.Msg("Plantilla predeterminada actualizada");
    }

    static async Task<IResult> Asignar(HttpContext c)
    {
        var u = c.Me(); var id = Api.Route(c, "id"); var b = await Api.Body(c);
        if (id == PlantillaSistema.Id || !Guid.TryParse(id, out _)) throw new AppError("Solo se pueden asignar plantillas propias", 400);
        var ids = (b["departamento_ids"] as JsonArray)?.Select(x => (int)Js.ParseFloat(x)).Distinct().ToList() ?? new();
        await Db.Tx(async tx =>
        {
            var ok = await tx.Query("SELECT id FROM plantillas_contrato WHERE admin_id = $1 AND id = $2 AND estado = 'lista'", u.Id, id);
            if (ok.First == null) throw new AppError("La plantilla no existe o aún no está lista", 400);
            await tx.Query("UPDATE departamentos SET plantilla_id = NULL WHERE admin_id = $1 AND plantilla_id = $2", u.Id, id);
            if (ids.Count > 0)
                await tx.Query("UPDATE departamentos SET plantilla_id = $2 WHERE admin_id = $1 AND id = ANY($3::int[])", u.Id, id, ids.Select(i => i.ToString()).ToList());
            return true;
        });
        return Api.Msg("Plantilla asignada");
    }

    static async Task<IResult> DescargarPlantilla(HttpContext c)
    {
        var u = c.Me(); var id = Api.Route(c, "id");
        var p = await ContratoServicio.CargarPlantilla(u.Id, id);
        return Docx(c, p.Docx, $"{p.Nombre}.docx");
    }

    static async Task<IResult> PreviewPlantilla(HttpContext c)
    {
        var u = c.Me(); var id = Api.Route(c, "id"); var b = await Api.Body(c);
        var p = await ContratoServicio.CargarPlantilla(u.Id, id);
        Dictionary<string, object?> data; JsonObject? extra = b["valores_extra"] as JsonObject;
        var inqId = b.S("inquilino_id");
        if (!string.IsNullOrEmpty(inqId)) data = (await ContratoServicio.CargarContexto(u.Id, inqId)).Data;
        else data = ContratoVariables.Demo();
        // Sin valores reales, los campos personalizados muestran su etiqueta como ejemplo.
        extra ??= new JsonObject();
        foreach (var campo in p.Campos)
        {
            var k = Js.Str(campo?["key"]); if (k != null && !extra.ContainsKey(k)) extra[k] = $"[{Js.Str(campo?["etiqueta"])}]";
        }
        var res = await ContratoRenderer.Generar(p.Docx, data, ContratoServicio.ValoresExtra(new JsonArray(p.Campos.Select(x => (JsonNode)x!.DeepClone()).ToArray()), extra));
        c.Response.Headers["X-Aproximado"] = res.Aproximado ? "1" : "0";
        return InquilinosEndpoints.PdfResponse(c, res.Pdf, "vista_previa.pdf");
    }

    // ── Generar contrato para un inquilino ──────────────────────────────────────────────────
    static async Task<IResult> Preparar(HttpContext c)
    {
        var u = c.Me(); var id = Api.Route(c, "id");
        var ctx = await ContratoServicio.CargarContexto(u.Id, id);
        var elegida = await ContratoServicio.ResolverPlantilla(u.Id, ctx.Inquilino, ctx.Depto);
        var origen = elegida.Sistema ? "sistema"
            : ctx.Inquilino.S("plantilla_id") == elegida.Id ? "inquilino"
            : ctx.Depto?.S("plantilla_id") == elegida.Id ? "propiedad" : "predeterminada";
        // Los datos faltantes dependen de la plantilla que el usuario esté mirando (puede ser distinta de la sugerida).
        var seleccionada = Api.Query(c, "plantilla_id") is { } pid ? await ContratoServicio.CargarPlantilla(u.Id, pid) : elegida;

        var disponibles = await Db.Query("SELECT id, nombre, campos, predeterminada FROM plantillas_contrato WHERE admin_id = $1 AND estado = 'lista' ORDER BY nombre", u.Id);
        var lista = new List<object?> { new Dictionary<string, object?> { ["id"] = PlantillaSistema.Id, ["nombre"] = PlantillaSistema.Nombre, ["campos"] = new JsonArray(), ["sistema"] = true } };
        lista.AddRange(disponibles.Rows.Select(x => (object?)new Dictionary<string, object?> { ["id"] = x.S("id"), ["nombre"] = x.S("nombre"), ["campos"] = x.J("campos"), ["sistema"] = false }));

        var versiones = await Db.Query(
            @"SELECT id, version, plantilla_nombre, fecha_generacion, aproximado FROM contratos
              WHERE admin_id = $1 AND inquilino_id = $2 ORDER BY version DESC", u.Id, id);

        return Api.Data(new Dictionary<string, object?>
        {
            ["inquilino"] = new Dictionary<string, object?> { ["id"] = id, ["nombre_completo"] = ctx.Inquilino.S("nombre_completo"), ["depto_numero"] = ctx.Inquilino.S("depto_numero") },
            ["plantilla_id"] = elegida.Id, ["origen"] = origen,
            ["plantillas"] = lista,
            ["faltantes"] = Faltantes(seleccionada, ctx),
            ["versiones"] = versiones.Rows,
            ["motor"] = LibreOfficeService.Disponible ? "libreoffice" : "aproximado",
        });
    }

    // Datos que la plantilla usa y que todavía no están en el sistema (solo los que el usuario puede completar en línea).
    static List<object?> Faltantes(PlantillaCargada p, ContratoContexto ctx)
    {
        var usadas = DocxService.VariablesPresentes(p.Docx).ToHashSet();
        var vars = ContratoVariables.Build(new ContractData(ctx.Data));
        var res = new List<object?>();
        foreach (var (key, (destino, campo, etiqueta)) in Completables)
        {
            if (!usadas.Contains(key)) continue;
            if (key == "fiador_domicilio" && string.IsNullOrEmpty(ctx.Inquilino.S("fiador_nombre"))) continue;
            if (!string.IsNullOrWhiteSpace(vars.GetValueOrDefault(key))) continue;
            res.Add(new Dictionary<string, object?> { ["key"] = key, ["etiqueta"] = etiqueta, ["destino"] = destino });
        }
        // La cuenta bancaria sale de Cuentas bancarias: no se completa aquí, pero se avisa.
        foreach (var (key, et) in new[] { ("banco_nombre", "Banco"), ("banco_clabe", "CLABE"), ("banco_titular", "Titular de la cuenta") })
            if (usadas.Contains(key) && string.IsNullOrWhiteSpace(vars.GetValueOrDefault(key)))
                res.Add(new Dictionary<string, object?> { ["key"] = key, ["etiqueta"] = et, ["destino"] = "cuenta" });
        return res;
    }

    static async Task<IResult> GenerarContrato(HttpContext c)
    {
        var u = c.Me(); var id = Api.Route(c, "id"); var b = await Api.Body(c);

        // 1) Completar los datos que faltaban (se guardan donde corresponde para la próxima vez)
        if (b["completar"] is JsonObject comp)
            foreach (var (key, nodo) in comp)
            {
                var valor = Js.Str(nodo)?.Trim() ?? "";
                if (valor.Length == 0 || !Completables.TryGetValue(key, out var def)) continue;
                if (valor.Length > 500) throw new AppError("Uno de los datos es demasiado largo", 400);
                switch (def.Destino)
                {
                    case "inquilino":
                        await Db.Query($"UPDATE inquilinos SET {def.Campo} = $3 WHERE admin_id = $1 AND id = $2", u.Id, id, valor);
                        break;
                    case "propiedad":
                        await Db.Query(
                            $"UPDATE departamentos SET {def.Campo} = $3 WHERE admin_id = $1 AND numero = (SELECT depto_numero FROM inquilinos WHERE admin_id = $1 AND id = $2)", u.Id, id, valor);
                        break;
                    case "config":
                        if (u.Rol != "admin") throw new AppError("Solo el administrador puede completar los datos del arrendador", 403);
                        await Db.Query(
                            @"INSERT INTO configuracion (admin_id, clave, valor) VALUES ($1, $2, $3)
                              ON CONFLICT (admin_id, clave) DO UPDATE SET valor = EXCLUDED.valor", u.Id, def.Campo, valor);
                        break;
                }
            }

        var r = await GenerarYGuardar(c, u, id, b.S("plantilla_id"), b["valores_extra"] as JsonObject);
        return Api.Data(r, 201);
    }

    // Genera TODOS los contratos pendientes (inquilinos activos sin contrato) con su plantilla resuelta.
    static async Task<IResult> GenerarPendientes(HttpContext c)
    {
        var u = c.Me();
        var pend = await Db.Query(
            @"SELECT i.id, i.nombre_completo FROM inquilinos i
              WHERE i.admin_id = $1 AND i.estado = 'activo'
                AND NOT EXISTS (SELECT 1 FROM contratos k WHERE k.admin_id = i.admin_id AND k.inquilino_id = i.id)
              ORDER BY i.nombre_completo", u.Id);
        int ok = 0; var fallos = new List<object?>();
        foreach (var f in pend.Rows)
        {
            try { await GenerarYGuardar(c, u, f.S("id")!, null, null); ok++; }
            catch (Exception e) { fallos.Add(new Dictionary<string, object?> { ["nombre"] = f.S("nombre_completo"), ["error"] = e.Message }); }
        }
        return Api.Data(new Dictionary<string, object?> { ["generados"] = ok, ["fallos"] = fallos });
    }

    static async Task<Dictionary<string, object?>> GenerarYGuardar(HttpContext c, AuthUser u, string id, string? plantillaId, JsonObject? extra)
    {
        // 2) Plantilla y datos
        var ctx = await ContratoServicio.CargarContexto(u.Id, id);
        var p = string.IsNullOrEmpty(plantillaId)
            ? await ContratoServicio.ResolverPlantilla(u.Id, ctx.Inquilino, ctx.Depto)
            : await ContratoServicio.CargarPlantilla(u.Id, plantillaId);

        var res = await ContratoServicio.Generar(p, ctx, extra);

        // 3) Historial: una fila por versión
        var snapshot = new JsonObject();
        foreach (var (k, v) in ContratoVariables.Build(new ContractData(ctx.Data))) snapshot[k] = v;
        var ins = await Db.Query(
            @"INSERT INTO contratos (inquilino_id, depto_numero, datos_contrato, generado_por, admin_id, plantilla_id, plantilla_nombre,
                                     version, docx, pdf, valores_extra, aproximado, plantilla_hash)
              VALUES ($1, $2, $3, $4, $5, $6, $7,
                      COALESCE((SELECT MAX(version) FROM contratos WHERE admin_id = $5 AND inquilino_id = $1), 0) + 1,
                      $8, $9, $10, $11, $12)
              RETURNING id, version",
            id, ctx.Inquilino.S("depto_numero"), snapshot.ToJsonString(), u.Actor, u.Id,
            p.Sistema ? null : p.Id, p.Nombre, res.Docx, res.Pdf, (extra ?? new JsonObject()).ToJsonString(), res.Aproximado,
            ContratoServicio.HashTexto(p.Docx));
        var fila = ins.First!;
        await Audit.Log(c, "contratos", "crear", fila.S("id")!, null,
            new Dictionary<string, object?> { ["inquilino_id"] = id, ["version"] = fila.S("version"), ["plantilla"] = p.Nombre });

        return new Dictionary<string, object?>
        {
            ["id"] = fila.S("id"), ["version"] = fila.I("version"), ["aproximado"] = res.Aproximado, ["plantilla_nombre"] = p.Nombre,
        };
    }

    // ── Listados y descargas ────────────────────────────────────────────────────────────────
    // Una fila por inquilino activo con el estado de su contrato (alimenta la pantalla /contratos).
    static async Task<IResult> Resumen(HttpContext c)
    {
        var u = c.Me();
        var r = await Db.Query(
            @"SELECT i.id, i.nombre_completo, i.depto_numero, i.renta, i.fecha_inicio, i.fecha_termino, i.deposito, i.estado,
                     i.plantilla_id AS inq_plantilla, d.plantilla_id AS depto_plantilla,
                     k.id AS contrato_id, k.version, k.plantilla_nombre, k.fecha_generacion, k.plantilla_hash
              FROM inquilinos i
              LEFT JOIN departamentos d ON d.admin_id = i.admin_id AND d.numero = i.depto_numero
              LEFT JOIN LATERAL (
                SELECT id, version, plantilla_nombre, fecha_generacion, plantilla_hash FROM contratos
                WHERE admin_id = $1 AND inquilino_id = i.id ORDER BY version DESC LIMIT 1) k ON TRUE
              WHERE i.admin_id = $1 AND i.estado = 'activo'
              ORDER BY i.fecha_termino ASC NULLS LAST, i.nombre_completo", u.Id);

        // ¿La plantilla que le toca hoy dice algo distinto de la que se usó al generar? Entonces el contrato quedó desactualizado.
        var pl = await Db.Query("SELECT id, estado, predeterminada, docx_plantilla FROM plantillas_contrato WHERE admin_id = $1 AND estado = 'lista'", u.Id);
        var hashes = pl.Rows.ToDictionary(x => x.S("id")!, x => ContratoServicio.HashTexto((byte[])x["docx_plantilla"]!));
        var defecto = pl.Rows.FirstOrDefault(x => x.B("predeterminada"))?.S("id");
        var hashSistema = ContratoServicio.HashTexto(PlantillaSistema.Docx);
        string HashActual(Row i)
        {
            foreach (var id in new[] { i.S("inq_plantilla"), i.S("depto_plantilla"), defecto })
                if (id != null && hashes.TryGetValue(id, out var h)) return h;
            return hashSistema;
        }
        var filas = r.Rows.Select(i => i.With(("desactualizado", i.S("contrato_id") != null && !string.IsNullOrEmpty(i.S("plantilla_hash")) && i.S("plantilla_hash") != HashActual(i)))
            .Without("plantilla_hash").Without("inq_plantilla").Without("depto_plantilla")).ToList();
        return Api.Data(filas);
    }

    static async Task<IResult> Historial(HttpContext c)
    {
        var u = c.Me(); var inq = Api.Query(c, "inquilino_id");
        var r = await Db.Query(
            @"SELECT k.id, k.inquilino_id, i.nombre_completo, k.depto_numero, k.version, k.plantilla_nombre, k.fecha_generacion, k.aproximado
              FROM contratos k JOIN inquilinos i ON i.id = k.inquilino_id
              WHERE k.admin_id = $1 AND ($2::uuid IS NULL OR k.inquilino_id = $2::uuid)
              ORDER BY k.fecha_generacion DESC LIMIT 200", u.Id, inq);
        return Api.Data(r.Rows);
    }

    static async Task<IResult> Archivo(HttpContext c)
    {
        var u = c.Me(); var id = Api.Route(c, "id");
        if (!Guid.TryParse(id, out _)) throw new AppError("Contrato no encontrado", 404);
        var formato = Api.Query(c, "formato") == "docx" ? "docx" : "pdf";
        var r = await Db.Query(
            @"SELECT k.docx, k.pdf, k.version, i.nombre_completo, k.depto_numero FROM contratos k
              JOIN inquilinos i ON i.id = k.inquilino_id WHERE k.admin_id = $1 AND k.id = $2", u.Id, id);
        var k = r.First ?? throw new AppError("Contrato no encontrado", 404);
        var nombre = Regex.Replace(k.S("nombre_completo") ?? "", @"\s+", "_");
        var baseName = $"contrato_{k.S("depto_numero")}_{nombre}_v{k.S("version")}";
        return formato == "docx" ? Docx(c, (byte[])k["docx"]!, baseName + ".docx") : InquilinosEndpoints.PdfResponse(c, (byte[])k["pdf"]!, baseName + ".pdf");
    }

    static async Task<IResult> EliminarContrato(HttpContext c)
    {
        var u = c.Me(); var id = Api.Route(c, "id");
        if (!Guid.TryParse(id, out _)) throw new AppError("Contrato no encontrado", 404);
        var r = await Db.Query("DELETE FROM contratos WHERE admin_id = $1 AND id = $2 RETURNING id, inquilino_id, version, plantilla_nombre", u.Id, id);
        if (r.First == null) throw new AppError("Contrato no encontrado", 404);
        await Audit.Log(c, "contratos", "eliminar", id, r.First, null);
        return Api.Msg("Contrato eliminado");
    }

    static IResult Docx(HttpContext c, byte[] bytes, string filename)
    {
        var ascii = Regex.Replace(filename.Normalize(System.Text.NormalizationForm.FormD), @"\p{M}", "");
        ascii = Regex.Replace(ascii, @"[^\x20-\x7E]|[""\\]", "_");
        c.Response.Headers.ContentDisposition = $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(filename)}";
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }
}
