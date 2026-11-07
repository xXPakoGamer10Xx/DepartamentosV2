namespace VertexRent.Api;

/// <summary>Todo el SQL del módulo de contratos y plantillas. Sin reglas de negocio ni HTTP.</summary>
internal static class ContratosRepository
{
    // ── Contexto para generar un contrato ────────────────────────────────────
    public static async Task<Row?> InquilinoDelPropietario(string adminId, string inquilinoId) =>
        (await Db.Query("SELECT * FROM inquilinos WHERE admin_id = $1 AND id = $2", adminId, inquilinoId)).First;

    public static async Task<List<Row>> ConfigArrendador(string adminId, string[] claves) =>
        (await Db.Query("SELECT clave, valor FROM configuracion WHERE admin_id = $1 AND clave = ANY($2)", adminId, claves)).Rows;

    public static async Task<Row?> Departamento(string adminId, string? numero) =>
        (await Db.Query(
            @"SELECT id, numero, descripcion, ubicacion, inventario_base, cuenta_bancaria_id, plantilla_id
              FROM departamentos WHERE admin_id = $1 AND numero = $2", adminId, numero)).First;

    public static async Task<Row?> CuentaParaDepartamento(string adminId, string? numero) =>
        (await Db.Query(
            @"SELECT cb.banco_nombre, cb.banco_clabe, cb.banco_titular
              FROM cuentas_bancarias cb
              WHERE cb.admin_id = $1 AND cb.activo = TRUE
                AND (cb.id = (SELECT d.cuenta_bancaria_id FROM departamentos d WHERE d.admin_id = $1 AND d.numero = $2)
                     OR cb.es_predeterminada = TRUE)
              ORDER BY (cb.id = (SELECT d.cuenta_bancaria_id FROM departamentos d WHERE d.admin_id = $1 AND d.numero = $2)) DESC
              LIMIT 1", adminId, numero)).First;

    // ── Plantillas ───────────────────────────────────────────────────────────
    public static async Task<Row?> PlantillaCargable(string adminId, string id) =>
        (await Db.Query("SELECT id, nombre, docx_plantilla, campos FROM plantillas_contrato WHERE admin_id = $1 AND id = $2", adminId, id)).First;

    /// <summary>La que corresponde a un inquilino: la suya → la de su propiedad → la predeterminada.</summary>
    public static async Task<Row?> PlantillaResuelta(string adminId, Guid? delInquilino, Guid? delDepto) =>
        (await Db.Query(
            @"SELECT id, nombre, docx_plantilla, campos FROM plantillas_contrato
              WHERE admin_id = $1 AND estado = 'lista' AND (id = $2 OR id = $3 OR predeterminada)
              ORDER BY (id = $2) DESC NULLS LAST, (id = $3) DESC NULLS LAST, predeterminada DESC
              LIMIT 1", adminId, delInquilino, delDepto)).First;

    public static async Task<List<Row>> ListarPlantillas(string adminId) =>
        (await Db.Query(
            @"SELECT p.id, p.nombre, p.descripcion, p.estado, p.predeterminada, p.campos, p.created_at, p.updated_at,
                     (SELECT COUNT(*)::int FROM departamentos d WHERE d.plantilla_id = p.id) AS propiedades,
                     (SELECT COUNT(*)::int FROM inquilinos i WHERE i.plantilla_id = p.id) AS inquilinos
              FROM plantillas_contrato p WHERE p.admin_id = $1 ORDER BY p.predeterminada DESC, p.created_at DESC", adminId)).Rows;

    public static async Task<List<Row>> PlantillasListas(string adminId) =>
        (await Db.Query("SELECT id, nombre, campos, predeterminada FROM plantillas_contrato WHERE admin_id = $1 AND estado = 'lista' ORDER BY nombre", adminId)).Rows;

    /// <summary>Plantillas listas con su docx, para comparar huellas en el resumen.</summary>
    public static async Task<List<Row>> PlantillasListasConDocx(string adminId) =>
        (await Db.Query("SELECT id, estado, predeterminada, docx_plantilla FROM plantillas_contrato WHERE admin_id = $1 AND estado = 'lista'", adminId)).Rows;

    public static async Task<Row?> PlantillaDetalle(string adminId, string id) =>
        (await Db.Query("SELECT id, nombre, estado, predeterminada, campos, marcas, docx_original FROM plantillas_contrato WHERE admin_id = $1 AND id = $2", adminId, id)).First;

    public static async Task<Row?> PlantillaParaGuardar(string adminId, string id) =>
        (await Db.Query("SELECT nombre, estado, campos, docx_original FROM plantillas_contrato WHERE admin_id = $1 AND id = $2", adminId, id)).First;

    public static async Task<string> InsertarPlantilla(string adminId, string nombre, byte[] original) =>
        (await Db.Query(
            @"INSERT INTO plantillas_contrato (admin_id, nombre, docx_original, docx_plantilla, estado)
              VALUES ($1, $2, $3, $3, 'borrador') RETURNING id", adminId, nombre, original)).First!.S("id")!;

    public static Task GuardarPlantilla(string adminId, string id, string nombre, string estado, string camposJson,
        string? marcasJson, byte[]? plantilla, bool? predeterminada) =>
        Db.Tx(async tx =>
        {
            if (predeterminada == true)
                await tx.Query("UPDATE plantillas_contrato SET predeterminada = FALSE WHERE admin_id = $1 AND id <> $2", adminId, id);
            await tx.Query(
                @"UPDATE plantillas_contrato SET nombre = $3, estado = $4, campos = $5,
                    marcas = COALESCE($6::jsonb, marcas), docx_plantilla = COALESCE($7, docx_plantilla),
                    predeterminada = COALESCE($8::boolean, predeterminada), updated_at = NOW()
                  WHERE admin_id = $1 AND id = $2",
                adminId, id, nombre, estado, camposJson, marcasJson, plantilla, predeterminada?.ToString().ToLowerInvariant());
            return true;
        });

    public static async Task<Row?> EliminarPlantilla(string adminId, string id) =>
        (await Db.Query("DELETE FROM plantillas_contrato WHERE admin_id = $1 AND id = $2 RETURNING id, nombre", adminId, id)).First;

    /// <summary>Quita la predeterminada actual y, si se indica una, la marca (debe estar lista); null deja la del sistema.</summary>
    public static Task CambiarPredeterminada(string adminId, string? plantillaId) =>
        Db.Tx(async tx =>
        {
            await tx.Query("UPDATE plantillas_contrato SET predeterminada = FALSE WHERE admin_id = $1", adminId);
            if (plantillaId != null)
            {
                var r = await tx.Query("UPDATE plantillas_contrato SET predeterminada = TRUE WHERE admin_id = $1 AND id = $2 AND estado = 'lista' RETURNING id", adminId, plantillaId);
                if (r.First == null) throw new AppError("La plantilla no existe o aún no está lista", 400);
            }
            return true;
        });

    public static Task AsignarAPropiedades(string adminId, string id, List<int> departamentoIds) =>
        Db.Tx(async tx =>
        {
            var ok = await tx.Query("SELECT id FROM plantillas_contrato WHERE admin_id = $1 AND id = $2 AND estado = 'lista'", adminId, id);
            if (ok.First == null) throw new AppError("La plantilla no existe o aún no está lista", 400);
            await tx.Query("UPDATE departamentos SET plantilla_id = NULL WHERE admin_id = $1 AND plantilla_id = $2", adminId, id);
            if (departamentoIds.Count > 0)
                await tx.Query("UPDATE departamentos SET plantilla_id = $2 WHERE admin_id = $1 AND id = ANY($3::int[])", adminId, id,
                    departamentoIds.Select(i => i.ToString()).ToList());
            return true;
        });

    // ── Datos que se completan al generar ────────────────────────────────────
    // `columna` viene de la lista fija Completables (nunca del usuario).
    public static Task CompletarInquilino(string columna, string adminId, string inquilinoId, string valor) =>
        Db.Query($"UPDATE inquilinos SET {columna} = $3 WHERE admin_id = $1 AND id = $2", adminId, inquilinoId, valor);

    public static Task CompletarPropiedad(string columna, string adminId, string inquilinoId, string valor) =>
        Db.Query($"UPDATE departamentos SET {columna} = $3 WHERE admin_id = $1 AND numero = (SELECT depto_numero FROM inquilinos WHERE admin_id = $1 AND id = $2)", adminId, inquilinoId, valor);

    // ── Contratos generados ──────────────────────────────────────────────────
    public static async Task<List<Row>> Versiones(string adminId, string inquilinoId) =>
        (await Db.Query(
            @"SELECT id, version, plantilla_nombre, fecha_generacion, aproximado FROM contratos
              WHERE admin_id = $1 AND inquilino_id = $2 ORDER BY version DESC", adminId, inquilinoId)).Rows;

    public static async Task<Row> InsertarContrato(string adminId, string inquilinoId, string? deptoNumero, string datosJson, string? actorId,
        string? plantillaId, string plantillaNombre, byte[] docx, byte[] pdf, string extraJson, bool aproximado, string hash) =>
        (await Db.Query(
            @"INSERT INTO contratos (inquilino_id, depto_numero, datos_contrato, generado_por, admin_id, plantilla_id, plantilla_nombre,
                                     version, docx, pdf, valores_extra, aproximado, plantilla_hash)
              VALUES ($1, $2, $3, $4, $5, $6, $7,
                      COALESCE((SELECT MAX(version) FROM contratos WHERE admin_id = $5 AND inquilino_id = $1), 0) + 1,
                      $8, $9, $10, $11, $12)
              RETURNING id, version",
            inquilinoId, deptoNumero, datosJson, actorId, adminId, plantillaId, plantillaNombre, docx, pdf, extraJson, aproximado, hash)).First!;

    public static async Task<List<Row>> InquilinosActivosSinContrato(string adminId) =>
        (await Db.Query(
            @"SELECT i.id, i.nombre_completo FROM inquilinos i
              WHERE i.admin_id = $1 AND i.estado = 'activo'
                AND NOT EXISTS (SELECT 1 FROM contratos k WHERE k.admin_id = i.admin_id AND k.inquilino_id = i.id)
              ORDER BY i.nombre_completo", adminId)).Rows;

    /// <summary>Una fila por inquilino activo con el estado de su último contrato.</summary>
    public static async Task<List<Row>> Resumen(string adminId) =>
        (await Db.Query(
            @"SELECT i.id, i.nombre_completo, i.depto_numero, i.renta, i.fecha_inicio, i.fecha_termino, i.deposito, i.estado,
                     i.plantilla_id AS inq_plantilla, d.plantilla_id AS depto_plantilla,
                     k.id AS contrato_id, k.version, k.plantilla_nombre, k.fecha_generacion, k.plantilla_hash
              FROM inquilinos i
              LEFT JOIN departamentos d ON d.admin_id = i.admin_id AND d.numero = i.depto_numero
              LEFT JOIN LATERAL (
                SELECT id, version, plantilla_nombre, fecha_generacion, plantilla_hash FROM contratos
                WHERE admin_id = $1 AND inquilino_id = i.id ORDER BY version DESC LIMIT 1) k ON TRUE
              WHERE i.admin_id = $1 AND i.estado = 'activo'
              ORDER BY i.fecha_termino ASC NULLS LAST, i.nombre_completo", adminId)).Rows;

    public static async Task<List<Row>> Historial(string adminId, string? inquilinoId) =>
        (await Db.Query(
            @"SELECT k.id, k.inquilino_id, i.nombre_completo, k.depto_numero, k.version, k.plantilla_nombre, k.fecha_generacion, k.aproximado
              FROM contratos k JOIN inquilinos i ON i.id = k.inquilino_id
              WHERE k.admin_id = $1 AND ($2::uuid IS NULL OR k.inquilino_id = $2::uuid)
              ORDER BY k.fecha_generacion DESC LIMIT 200", adminId, inquilinoId)).Rows;

    public static async Task<Row?> ArchivoDeContrato(string adminId, string id) =>
        (await Db.Query(
            @"SELECT k.docx, k.pdf, k.version, i.nombre_completo, k.depto_numero FROM contratos k
              JOIN inquilinos i ON i.id = k.inquilino_id WHERE k.admin_id = $1 AND k.id = $2", adminId, id)).First;

    public static async Task<Row?> EliminarContrato(string adminId, string id) =>
        (await Db.Query("DELETE FROM contratos WHERE admin_id = $1 AND id = $2 RETURNING id, inquilino_id, version, plantilla_nombre", adminId, id)).First;

    public static async Task<byte[]?> UltimoPdf(string adminId, string inquilinoId) =>
        (await Db.Query("SELECT pdf FROM contratos WHERE admin_id = $1 AND inquilino_id = $2 AND pdf IS NOT NULL ORDER BY version DESC LIMIT 1", adminId, inquilinoId))
            .First?["pdf"] as byte[];
}
