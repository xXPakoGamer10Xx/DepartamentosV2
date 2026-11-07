using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VertexRent.Api;

/// <summary>Plantillas y contratos generados. No conoce HTTP; el SQL vive en <see cref="ContratosRepository"/>.</summary>
public static class ContratosService
{
    // Datos que se pueden completar al generar un contrato: variable → (destino, columna, etiqueta)
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

    // ── Catálogo ─────────────────────────────────────────────────────────────
    public static Dictionary<string, object?> Variables() => new()
    {
        ["variables"] = ContratoVariables.Todas.Select(v => new Dictionary<string, object?>
        {
            ["key"] = v.Key, ["etiqueta"] = v.Etiqueta, ["grupo"] = v.Grupo, ["ejemplo"] = v.Ejemplo,
        }).ToList(),
        ["motor"] = LibreOfficeService.Disponible ? "libreoffice" : "aproximado",
        ["ia"] = ContratoAnalisisService.Disponible,
    };

    // ── Utilidades de plantillas ─────────────────────────────────────────────
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
        (n as JsonArray)?.Select(x => new Marca(Js.Str(x?["p"]) ?? "", (int)Js.ParseFloat(x?["ini"]), (int)Js.ParseFloat(x?["fin"]), Js.Str(x?["key"]) ?? "",
            (x?["borrar"] as JsonArray)?.Select(b => Js.Str(b) ?? "").Where(b => b.Length > 0).ToArray())).ToList() ?? new();

    // ── Plantillas ───────────────────────────────────────────────────────────
    public static async Task<List<object?>> ListarPlantillas(AuthUser u)
    {
        var propias = await ContratosRepository.ListarPlantillas(u.Id);
        var hayPropia = propias.Any(x => x.B("predeterminada"));
        var lista = new List<object?>
        {
            new Dictionary<string, object?>
            {
                ["id"] = PlantillaSistema.Id, ["nombre"] = PlantillaSistema.Nombre, ["descripcion"] = PlantillaSistema.Descripcion,
                ["estado"] = "lista", ["sistema"] = true, ["predeterminada"] = !hayPropia, ["campos"] = new JsonArray(),
                ["propiedades"] = 0, ["inquilinos"] = 0,
            },
        };
        lista.AddRange(propias.Select(x => (object?)x.With(("sistema", false))));
        return lista;
    }

    /// <summary>Sube el Word (o PDF), lo valida y propone dónde van los datos (IA). Queda como borrador hasta guardarlo.</summary>
    public static async Task<Dictionary<string, object?>> SubirPlantilla(AuthUser u, string? ip, JsonObject b)
    {
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

        var id = await ContratosRepository.InsertarPlantilla(u.Id, nombre, original);
        await Audit.Log(u, ip, "plantillas_contrato", "crear", id, null, new Dictionary<string, object?> { ["nombre"] = nombre });
        return new()
        {
            ["id"] = id, ["nombre"] = nombre,
            ["html"] = DocxService.HtmlDeRevision(original, Array.Empty<Marca>()),
            ["sugerencias"] = sugerencias.Select(s => s.ToJson()).ToList(),
            ["variables_presentes"] = presentes,
            ["ia_error"] = iaError, ["desde_pdf"] = desdePdf, ["aviso_conversion"] = avisoPdf,
        };
    }

    public static async Task<Dictionary<string, object?>> DetallePlantilla(AuthUser u, string id)
    {
        if (id == PlantillaSistema.Id)
            return new()
            {
                ["id"] = PlantillaSistema.Id, ["nombre"] = PlantillaSistema.Nombre, ["sistema"] = true, ["estado"] = "lista",
                ["campos"] = new JsonArray(), ["marcas"] = new JsonArray(),
                ["html"] = DocxService.HtmlDeRevision(PlantillaSistema.Docx, Array.Empty<Marca>()),
            };
        if (!Guid.TryParse(id, out _)) throw new AppError("Plantilla no encontrada", 404);
        var p = await ContratosRepository.PlantillaDetalle(u.Id, id) ?? throw new AppError("Plantilla no encontrada", 404);
        var original = (byte[])p["docx_original"]!;
        var campos = p.J("campos") as JsonArray ?? new JsonArray();
        var marcas = LeerMarcas(p.J("marcas"));
        var textos = DocxService.ExtraerParrafos(original).ToDictionary(x => x.Id, x => x.Texto);
        return new()
        {
            ["id"] = id, ["nombre"] = p.S("nombre"), ["sistema"] = false, ["estado"] = p.S("estado"), ["predeterminada"] = p.B("predeterminada"),
            ["campos"] = campos,
            ["marcas"] = new JsonArray(marcas.Select(m => (JsonNode)MarcaJson(m, textos.TryGetValue(m.P, out var t) && m.Fin <= t.Length ? t[m.Ini..m.Fin] : "", campos)).ToArray()),
            ["html"] = DocxService.HtmlDeRevision(original, Array.Empty<Marca>()),
            ["variables_presentes"] = DocxService.VariablesPresentes(original),
        };
    }

    public static async Task<Dictionary<string, object?>> GuardarPlantilla(AuthUser u, string? ip, string id, JsonObject b)
    {
        if (id == PlantillaSistema.Id) throw new AppError("La plantilla del sistema no se puede modificar. Descárgala y sube tu versión.", 400);
        if (!Guid.TryParse(id, out _)) throw new AppError("Plantilla no encontrada", 404);
        var cur = await ContratosRepository.PlantillaParaGuardar(u.Id, id) ?? throw new AppError("Plantilla no encontrada", 404);
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

        await ContratosRepository.GuardarPlantilla(u.Id, id, nombre, estado, campos.ToJsonString(), marcasJson?.ToJsonString(), plantilla, predeterminada);
        await Audit.Log(u, ip, "plantillas_contrato", "editar", id, new Dictionary<string, object?> { ["nombre"] = cur.S("nombre") },
            new Dictionary<string, object?> { ["nombre"] = nombre, ["estado"] = estado });
        return new() { ["id"] = id, ["nombre"] = nombre, ["estado"] = estado };
    }

    public static async Task EliminarPlantilla(AuthUser u, string? ip, string id)
    {
        if (id == PlantillaSistema.Id) throw new AppError("La plantilla del sistema no se puede eliminar", 400);
        if (!Guid.TryParse(id, out _)) throw new AppError("Plantilla no encontrada", 404);
        var borrada = await ContratosRepository.EliminarPlantilla(u.Id, id) ?? throw new AppError("Plantilla no encontrada", 404);
        await Audit.Log(u, ip, "plantillas_contrato", "eliminar", id, borrada, null);
    }

    public static async Task HacerPredeterminada(AuthUser u, string id)
    {
        if (id != PlantillaSistema.Id && !Guid.TryParse(id, out _)) throw new AppError("Plantilla no encontrada", 404);
        await ContratosRepository.CambiarPredeterminada(u.Id, id == PlantillaSistema.Id ? null : id);
    }

    public static async Task Asignar(AuthUser u, string id, JsonObject b)
    {
        if (id == PlantillaSistema.Id || !Guid.TryParse(id, out _)) throw new AppError("Solo se pueden asignar plantillas propias", 400);
        var ids = (b["departamento_ids"] as JsonArray)?.Select(x => (int)Js.ParseFloat(x)).Distinct().ToList() ?? new();
        await ContratosRepository.AsignarAPropiedades(u.Id, id, ids);
    }

    public static async Task<(byte[] Docx, string Nombre)> DescargarPlantilla(AuthUser u, string id)
    {
        var p = await ContratoServicio.CargarPlantilla(u.Id, id);
        return (p.Docx, $"{p.Nombre}.docx");
    }

    public static async Task<(byte[] Pdf, bool Aproximado)> VistaPrevia(AuthUser u, string id, JsonObject b)
    {
        var p = await ContratoServicio.CargarPlantilla(u.Id, id);
        var extra = b["valores_extra"] as JsonObject ?? new JsonObject();
        var inquilinoId = b.S("inquilino_id");
        var data = !string.IsNullOrEmpty(inquilinoId) ? (await ContratoServicio.CargarContexto(u.Id, inquilinoId)).Data : ContratoVariables.Demo();
        // Sin valores reales, los campos personalizados muestran su etiqueta como ejemplo.
        foreach (var campo in p.Campos)
        {
            var k = Js.Str(campo?["key"]); if (k != null && !extra.ContainsKey(k)) extra[k] = $"[{Js.Str(campo?["etiqueta"])}]";
        }
        var res = await ContratoRenderer.Generar(p.Docx, data, ContratoServicio.ValoresExtra(new JsonArray(p.Campos.Select(x => (JsonNode)x!.DeepClone()).ToArray()), extra));
        return (res.Pdf, res.Aproximado);
    }

    // ── Generar contrato para un inquilino ───────────────────────────────────
    public static async Task<Dictionary<string, object?>> Preparar(AuthUser u, string inquilinoId, string? plantillaId)
    {
        var ctx = await ContratoServicio.CargarContexto(u.Id, inquilinoId);
        var elegida = await ContratoServicio.ResolverPlantilla(u.Id, ctx.Inquilino, ctx.Depto);
        var origen = elegida.Sistema ? "sistema"
            : ctx.Inquilino.S("plantilla_id") == elegida.Id ? "inquilino"
            : ctx.Depto?.S("plantilla_id") == elegida.Id ? "propiedad" : "predeterminada";
        // Los datos faltantes dependen de la plantilla que el usuario esté mirando (puede ser distinta de la sugerida).
        var seleccionada = plantillaId != null ? await ContratoServicio.CargarPlantilla(u.Id, plantillaId) : elegida;

        var disponibles = await ContratosRepository.PlantillasListas(u.Id);
        var lista = new List<object?> { new Dictionary<string, object?> { ["id"] = PlantillaSistema.Id, ["nombre"] = PlantillaSistema.Nombre, ["campos"] = new JsonArray(), ["sistema"] = true } };
        lista.AddRange(disponibles.Select(x => (object?)new Dictionary<string, object?> { ["id"] = x.S("id"), ["nombre"] = x.S("nombre"), ["campos"] = x.J("campos"), ["sistema"] = false }));

        return new()
        {
            ["inquilino"] = new Dictionary<string, object?> { ["id"] = inquilinoId, ["nombre_completo"] = ctx.Inquilino.S("nombre_completo"), ["depto_numero"] = ctx.Inquilino.S("depto_numero") },
            ["plantilla_id"] = elegida.Id, ["origen"] = origen,
            ["plantillas"] = lista,
            ["faltantes"] = Faltantes(seleccionada, ctx),
            ["versiones"] = await ContratosRepository.Versiones(u.Id, inquilinoId),
            ["motor"] = LibreOfficeService.Disponible ? "libreoffice" : "aproximado",
        };
    }

    /// <summary>Datos que la plantilla usa y que todavía no están en el sistema (solo los que el usuario puede completar en línea).</summary>
    static List<object?> Faltantes(PlantillaCargada p, ContratoContexto ctx)
    {
        var usadas = DocxService.VariablesPresentes(p.Docx).ToHashSet();
        var vars = ContratoVariables.Build(new ContractData(ctx.Data));
        var res = new List<object?>();
        foreach (var (key, (destino, _, etiqueta)) in Completables)
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

    public static async Task<Dictionary<string, object?>> Generar(AuthUser u, string? ip, string inquilinoId, JsonObject b)
    {
        // 1) Completar los datos que faltaban (se guardan donde corresponde para la próxima vez)
        if (b["completar"] is JsonObject comp)
            foreach (var (key, nodo) in comp)
            {
                var valor = Js.Str(nodo)?.Trim() ?? "";
                if (valor.Length == 0 || !Completables.TryGetValue(key, out var def)) continue;
                if (valor.Length > 500) throw new AppError("Uno de los datos es demasiado largo", 400);
                switch (def.Destino)
                {
                    case "inquilino": await ContratosRepository.CompletarInquilino(def.Campo, u.Id, inquilinoId, valor); break;
                    case "propiedad": await ContratosRepository.CompletarPropiedad(def.Campo, u.Id, inquilinoId, valor); break;
                    case "config":
                        if (u.Rol != "admin") throw new AppError("Solo el administrador puede completar los datos del arrendador", 403);
                        await ConfigRepository.Guardar(u.Id, def.Campo, valor);
                        break;
                }
            }
        return await GenerarYGuardar(u, ip, inquilinoId, b.S("plantilla_id"), b["valores_extra"] as JsonObject);
    }

    /// <summary>Genera TODOS los contratos pendientes (inquilinos activos sin contrato) con su plantilla resuelta.</summary>
    public static async Task<Dictionary<string, object?>> GenerarPendientes(AuthUser u, string? ip)
    {
        int ok = 0; var fallos = new List<object?>();
        foreach (var f in await ContratosRepository.InquilinosActivosSinContrato(u.Id))
        {
            try { await GenerarYGuardar(u, ip, f.S("id")!, null, null); ok++; }
            catch (Exception e) { fallos.Add(new Dictionary<string, object?> { ["nombre"] = f.S("nombre_completo"), ["error"] = e.Message }); }
        }
        return new() { ["generados"] = ok, ["fallos"] = fallos };
    }

    static async Task<Dictionary<string, object?>> GenerarYGuardar(AuthUser u, string? ip, string id, string? plantillaId, JsonObject? extra)
    {
        var ctx = await ContratoServicio.CargarContexto(u.Id, id);
        var p = string.IsNullOrEmpty(plantillaId)
            ? await ContratoServicio.ResolverPlantilla(u.Id, ctx.Inquilino, ctx.Depto)
            : await ContratoServicio.CargarPlantilla(u.Id, plantillaId);
        var res = await ContratoServicio.Generar(p, ctx, extra);

        // Historial: una fila por versión
        var snapshot = new JsonObject();
        foreach (var (k, v) in ContratoVariables.Build(new ContractData(ctx.Data))) snapshot[k] = v;
        var fila = await ContratosRepository.InsertarContrato(u.Id, id, ctx.Inquilino.S("depto_numero"), snapshot.ToJsonString(), u.Actor,
            p.Sistema ? null : p.Id, p.Nombre, res.Docx, res.Pdf, (extra ?? new JsonObject()).ToJsonString(), res.Aproximado, ContratoServicio.HashTexto(p.Docx));
        await Audit.Log(u, ip, "contratos", "crear", fila.S("id")!, null,
            new Dictionary<string, object?> { ["inquilino_id"] = id, ["version"] = fila.S("version"), ["plantilla"] = p.Nombre });

        return new() { ["id"] = fila.S("id"), ["version"] = fila.I("version"), ["aproximado"] = res.Aproximado, ["plantilla_nombre"] = p.Nombre };
    }

    // ── Listados y descargas ─────────────────────────────────────────────────
    /// <summary>Una fila por inquilino activo con el estado de su contrato; marca los que quedaron desactualizados respecto a su plantilla actual.</summary>
    public static async Task<List<Row>> Resumen(AuthUser u)
    {
        var filas = await ContratosRepository.Resumen(u.Id);
        var plantillas = await ContratosRepository.PlantillasListasConDocx(u.Id);
        var hashes = plantillas.ToDictionary(x => x.S("id")!, x => ContratoServicio.HashTexto((byte[])x["docx_plantilla"]!));
        var defecto = plantillas.FirstOrDefault(x => x.B("predeterminada"))?.S("id");
        var hashSistema = ContratoServicio.HashTexto(PlantillaSistema.Docx);
        string HashActual(Row i)
        {
            foreach (var id in new[] { i.S("inq_plantilla"), i.S("depto_plantilla"), defecto })
                if (id != null && hashes.TryGetValue(id, out var h)) return h;
            return hashSistema;
        }
        return filas.Select(i => i.With(("desactualizado", i.S("contrato_id") != null && !string.IsNullOrEmpty(i.S("plantilla_hash")) && i.S("plantilla_hash") != HashActual(i)))
            .Without("plantilla_hash").Without("inq_plantilla").Without("depto_plantilla")).ToList();
    }

    public static Task<List<Row>> Historial(AuthUser u, string? inquilinoId) => ContratosRepository.Historial(u.Id, inquilinoId);

    public static async Task<(byte[] Bytes, string Nombre, bool EsDocx)> Archivo(AuthUser u, string id, string? formato)
    {
        if (!Guid.TryParse(id, out _)) throw new AppError("Contrato no encontrado", 404);
        var k = await ContratosRepository.ArchivoDeContrato(u.Id, id) ?? throw new AppError("Contrato no encontrado", 404);
        var nombre = Regex.Replace(k.S("nombre_completo") ?? "", @"\s+", "_");
        var baseName = $"contrato_{k.S("depto_numero")}_{nombre}_v{k.S("version")}";
        return formato == "docx" ? ((byte[])k["docx"]!, baseName + ".docx", true) : ((byte[])k["pdf"]!, baseName + ".pdf", false);
    }

    public static async Task Eliminar(AuthUser u, string? ip, string id)
    {
        if (!Guid.TryParse(id, out _)) throw new AppError("Contrato no encontrado", 404);
        var borrado = await ContratosRepository.EliminarContrato(u.Id, id) ?? throw new AppError("Contrato no encontrado", 404);
        await Audit.Log(u, ip, "contratos", "eliminar", id, borrado, null);
    }
}
