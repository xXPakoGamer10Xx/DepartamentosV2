using System.Text;

namespace VertexRent.Api;

/// <summary>Valores de las columnas editables de un inquilino (alta y edición).</summary>
internal sealed record InquilinoDatos(
    object? Nombre, object? Depto, object? Renta, object? RentaLetra, object? Deposito, object? FechaPago,
    object? FechaInicio, object? FechaTermino, object? Fiador, object? Tel, object? TelFiador, object? Observaciones,
    string? Inventario, object? DepTipo, string DepFechas, object? Metodo,
    string? Domicilio, string? Identificacion, string? EmailContacto, string? FiadorDomicilio, object? Estado = null);

/// <summary>Todo el SQL del módulo de inquilinos. Sin reglas de negocio ni HTTP.</summary>
internal static class InquilinosRepository
{
    public static async Task<Row?> MiDepto(string usuarioId) =>
        (await Db.Query("SELECT i.* FROM inquilinos i WHERE i.usuario_id = $1 AND i.estado = 'activo' LIMIT 1", usuarioId)).First;

    public static async Task<QueryResult> Listar(string adminId, string? search, string? estado, string? depto)
    {
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
        var ps = new List<object?> { adminId };
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
        return await Db.Query(sql.ToString(), ps.ToArray());
    }

    public static async Task<Row?> PorIdConHistorial(string adminId, string id) =>
        (await Db.Query(
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
              FROM inquilinos i WHERE i.admin_id = $1 AND i.id = $2", adminId, id)).First;

    public static async Task<Row?> Obtener(string adminId, string id) =>
        (await Db.Query("SELECT * FROM inquilinos WHERE admin_id = $1 AND id = $2", adminId, id)).First;

    public static async Task<bool> Existe(string adminId, string id) =>
        (await Db.Query("SELECT id FROM inquilinos WHERE admin_id = $1 AND id = $2", adminId, id)).First != null;

    public static async Task<bool> ExisteDepartamento(string adminId, object? numero) =>
        (await Db.Query("SELECT numero FROM departamentos WHERE admin_id = $1 AND numero = $2", adminId, numero)).First != null;

    public static async Task<Row> Insertar(string adminId, string invitationToken, InquilinoDatos d) =>
        (await Db.Query(
            @"INSERT INTO inquilinos (
                admin_id, invitation_token,
                nombre_completo, depto_numero, renta, renta_letra, deposito, fecha_pago,
                fecha_inicio, fecha_termino, fiador_nombre, tel_arrendatario,
                fiador_telefono, observaciones, inventario, deposito_tipo, deposito_fechas,
                metodo_pago, domicilio, identificacion, email_contacto, fiador_domicilio
              ) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,$22)
              RETURNING *",
            adminId, invitationToken, d.Nombre, d.Depto, d.Renta, d.RentaLetra, d.Deposito, d.FechaPago,
            d.FechaInicio, d.FechaTermino, d.Fiador, d.Tel, d.TelFiador, d.Observaciones, d.Inventario, d.DepTipo, d.DepFechas,
            d.Metodo, d.Domicilio, d.Identificacion, d.EmailContacto, d.FiadorDomicilio)).First!;

    public static async Task<Row> Actualizar(string adminId, string id, InquilinoDatos d) =>
        (await Db.Query(
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
            d.Nombre, d.Depto, d.Renta, d.RentaLetra, d.Deposito, d.FechaPago, d.FechaInicio, d.FechaTermino,
            d.Fiador, d.Tel, d.TelFiador, d.Observaciones, d.Inventario, d.Estado, d.DepTipo, d.DepFechas, d.Metodo,
            adminId, id, d.Domicilio, d.Identificacion, d.EmailContacto, d.FiadorDomicilio)).First!;

    /// <summary>Baja lógica: se conserva el historial.</summary>
    public static Task DarDeBaja(string adminId, string id) =>
        Db.Query("UPDATE inquilinos SET estado = 'inactivo' WHERE admin_id = $1 AND id = $2", adminId, id);

    public static async Task<bool> HayOtrosActivosEnDepto(string adminId, string? depto, string exceptoId) =>
        (await Db.Query(
            "SELECT id FROM inquilinos WHERE admin_id = $1 AND depto_numero = $2 AND estado = 'activo' AND id != $3",
            adminId, depto, exceptoId)).Rows.Count > 0;

    public static Task SetEstadoDepartamento(string adminId, string? numero, string estado) =>
        Db.Query("UPDATE departamentos SET estado = $3 WHERE admin_id = $1 AND numero = $2", adminId, numero, estado);

    public static async Task<bool> CuentaDeInquilinoDelPropietario(object? usuarioId, string adminId) =>
        (await Db.Query("SELECT 1 FROM usuarios WHERE id = $1 AND rol = 'inquilino' AND admin_id = $2", usuarioId, adminId)).First != null;

    public static async Task<Row?> Vincular(string adminId, string id, object? usuarioId) =>
        (await Db.Query(
            "UPDATE inquilinos SET usuario_id = $1 WHERE admin_id = $2 AND id = $3 RETURNING id, nombre_completo, usuario_id",
            usuarioId, adminId, id)).First;

    public static async Task<byte[]?> UltimoPdfGenerado(string adminId, string id) =>
        (await Db.Query("SELECT pdf FROM contratos WHERE admin_id = $1 AND inquilino_id = $2 AND pdf IS NOT NULL ORDER BY version DESC LIMIT 1", adminId, id))
            .First?["pdf"] as byte[];
}
