namespace VertexRent.Api;

public static class TicketsEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/tickets").Auth();

        // El inquilino siempre puede ver/crear/borrar sus propios tickets;
        // admin/cobrador necesitan el permiso 'tickets'.
        g.MapGet("/", H(GetTickets)).InquilinoOrPermiso("tickets");
        g.MapPost("/", H(CreateTicket));
        g.MapPatch("/{id}", H(UpdateTicket)).Permiso("tickets");
        g.MapDelete("/{id}", H(DeleteTicket)).InquilinoOrPermiso("tickets");
    }

    static async Task<IResult> GetTickets(HttpContext c)
    {
        var u = c.Me();
        QueryResult result;

        if (u.Rol == "admin" || u.Rol == "cobrador")
        {
            var estado = Api.Query(c, "estado");
            var mes = Api.Query(c, "mes");
            const string select = @"
                SELECT t.*,
                  i.nombre_completo as inquilino_nombre,
                  i.depto_numero,
                  u.nombre_completo as atendido_por_nombre
                FROM tickets t
                JOIN inquilinos i ON i.id = t.inquilino_id
                LEFT JOIN usuarios u ON u.id = t.atendido_por
                WHERE i.admin_id = $1";

            if (mes != null)
            {
                var parts = mes.Split('-');
                var sql = select + @"
                    AND DATE_PART('year', t.created_at) = $2
                    AND DATE_PART('month', t.created_at) = $3";
                var ps = new List<object?> { u.Id, parts[0], parts.Length > 1 ? parts[1] : null };
                if (estado != null) { sql += " AND t.estado = $4"; ps.Add(estado); }
                sql += " ORDER BY t.created_at DESC";
                result = await Db.Query(sql, ps.ToArray());
            }
            else
            {
                var sql = select;
                var ps = new List<object?> { u.Id };
                if (estado != null) { sql += " AND t.estado = $2"; ps.Add(estado); }
                sql += @"
                    ORDER BY
                      CASE t.estado WHEN 'abierto' THEN 0 WHEN 'en_revision' THEN 1 ELSE 2 END,
                      t.created_at DESC";
                result = await Db.Query(sql, ps.ToArray());
            }
        }
        else
        {
            var inq = await Db.Query("SELECT id FROM inquilinos WHERE usuario_id = $1 AND estado = 'activo' LIMIT 1", u.Id);
            if (inq.First == null) return Api.Data(new List<object>());
            result = await Db.Query(
                "SELECT t.* FROM tickets t WHERE t.inquilino_id = $1 ORDER BY t.created_at DESC", inq.First.S("id"));
        }
        return Api.Data(result.Rows);
    }

    static async Task<IResult> CreateTicket(HttpContext c)
    {
        var b = await Api.Body(c);
        var u = c.Me();
        var titulo = b.S("titulo")?.Trim();
        var descripcion = b.S("descripcion")?.Trim();
        if (string.IsNullOrEmpty(titulo) || string.IsNullOrEmpty(descripcion))
            throw new AppError("Título y descripción son requeridos", 400);

        string? inqId = b.S("inquilino_id");
        if (u.Rol == "inquilino")
        {
            var inq = await Db.Query("SELECT id FROM inquilinos WHERE usuario_id = $1 AND estado = 'activo' LIMIT 1", u.Id);
            if (inq.First == null) throw new AppError("Tu cuenta no está vinculada a ningún departamento", 403);
            inqId = inq.First.S("id");
        }
        else
        {
            var inq = await Db.Query("SELECT id FROM inquilinos WHERE id = $1 AND admin_id = $2", inqId, u.Id);
            if (inq.First == null) throw new AppError("Inquilino no encontrado o no autorizado", 403);
        }
        if (string.IsNullOrEmpty(inqId)) throw new AppError("inquilino_id es requerido", 400);

        var r = await Db.Query(
            "INSERT INTO tickets (inquilino_id, titulo, descripcion, creado_por) VALUES ($1, $2, $3, $4) RETURNING *",
            inqId, titulo, descripcion, u.Actor);
        var ticket = r.First!;

        // Notificar al admin en tiempo real (SSE + push), sin bloquear la respuesta.
        var ticketId = ticket.S("id");
        _ = Task.Run(async () =>
        {
            try
            {
                var enriched = await Db.Query(
                    @"SELECT t.*, i.nombre_completo as inquilino_nombre, i.depto_numero, i.admin_id
                      FROM tickets t JOIN inquilinos i ON i.id = t.inquilino_id WHERE t.id = $1", ticketId);
                var full = enriched.First;
                var adminId = full?.S("admin_id");
                if (full != null && !string.IsNullOrEmpty(adminId))
                {
                    SseService.EmitToUser(adminId, "ticket_new", new { ticket = full });
                    await PushService.CreateAndSendNotification(adminId, "🔔 Nuevo ticket",
                        $"{full.S("titulo")} — Depto {full.S("depto_numero")}", "ticket");
                }
            }
            catch { }
        });

        return Api.Data(ticket, 201);
    }

    static async Task<IResult> UpdateTicket(HttpContext c)
    {
        var id = Api.Route(c, "id");
        var b = await Api.Body(c);
        var u = c.Me();
        var estado = Js.T(b["estado"]) ? b.S("estado") : null;
        var nota = b.ContainsKey("nota_admin") ? b["nota_admin"] is null ? null : b.S("nota_admin") : null;

        var check = await Db.Query(
            "SELECT t.id FROM tickets t JOIN inquilinos i ON i.id = t.inquilino_id WHERE t.id = $1 AND i.admin_id = $2", id, u.Id);
        if (check.First == null) throw new AppError("Ticket no encontrado o no autorizado", 404);

        var r = await Db.Query(
            @"UPDATE tickets SET
                estado       = COALESCE($1, estado),
                nota_admin   = COALESCE($2, nota_admin),
                atendido_por = CASE WHEN $1 IS NOT NULL THEN $3 ELSE atendido_por END,
                updated_at   = NOW()
              WHERE id = $4 RETURNING *", estado, nota, u.Actor, id);
        var updated = r.First!;

        SseService.EmitToUser(u.Id, "ticket_updated", new { ticket = updated });
        _ = Task.Run(async () =>
        {
            try
            {
                var inq = await Db.Query(
                    "SELECT i.usuario_id FROM inquilinos i JOIN tickets t ON t.inquilino_id = i.id WHERE t.id = $1", id);
                var usuarioId = inq.First?.S("usuario_id");
                if (!string.IsNullOrEmpty(usuarioId))
                {
                    SseService.EmitToUser(usuarioId, "ticket_updated", new { ticket = updated });
                    var label = estado == "en_revision" ? "en revisión" : estado == "resuelto" ? "resuelto" : "actualizado";
                    await PushService.CreateAndSendNotification(usuarioId, "📋 Ticket actualizado",
                        $"Tu reporte fue marcado como {label}", "ticket");
                }
            }
            catch { }
        });

        return Api.Data(updated);
    }

    static async Task<IResult> DeleteTicket(HttpContext c)
    {
        var id = Api.Route(c, "id");
        var u = c.Me();

        if (u.Rol == "admin" || u.Rol == "cobrador")
        {
            var r = await Db.Query(
                "DELETE FROM tickets t USING inquilinos i WHERE t.id = $1 AND t.inquilino_id = i.id AND i.admin_id = $2 RETURNING t.id",
                id, u.Id);
            if (r.First == null) throw new AppError("Ticket no encontrado o no autorizado", 404);
            return Api.Ok();
        }

        var inq = await Db.Query("SELECT id FROM inquilinos WHERE usuario_id = $1 AND estado = 'activo' LIMIT 1", u.Id);
        if (inq.First == null) throw new AppError("No autorizado", 403);

        var del = await Db.Query(
            @"DELETE FROM tickets
              WHERE id = $1 AND inquilino_id = $2 AND created_at > NOW() - INTERVAL '5 minutes'
              RETURNING id", id, inq.First.S("id"));
        if (del.First == null) throw new AppError("No puedes eliminar este ticket (ya pasaron 5 minutos o no es tuyo)", 403);
        return Api.Ok();
    }
}
