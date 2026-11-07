using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Reglas de los tickets de incidencias. El SQL vive en <see cref="TicketsRepository"/>.</summary>
public static class TicketsService
{
    public static async Task<List<Row>> Listar(AuthUser u, string? estado, string? mes)
    {
        if (u.Rol is "admin" or "cobrador") return await TicketsRepository.DelPropietario(u.Id, estado, mes);
        var inquilinoId = await TicketsRepository.InquilinoActivoDelUsuario(u.Id);
        return inquilinoId == null ? new List<Row>() : await TicketsRepository.DelInquilino(inquilinoId);
    }

    public static async Task<Row> Crear(AuthUser u, JsonObject b)
    {
        var titulo = b.S("titulo")?.Trim();
        var descripcion = b.S("descripcion")?.Trim();
        if (string.IsNullOrEmpty(titulo) || string.IsNullOrEmpty(descripcion))
            throw new AppError("Título y descripción son requeridos", 400);

        string? inquilinoId = b.S("inquilino_id");
        if (u.Rol == "inquilino")
        {
            inquilinoId = await TicketsRepository.InquilinoActivoDelUsuario(u.Id)
                ?? throw new AppError("Tu cuenta no está vinculada a ningún departamento", 403);
        }
        else if (!await TicketsRepository.InquilinoDelPropietario(inquilinoId, u.Id))
            throw new AppError("Inquilino no encontrado o no autorizado", 403);
        if (string.IsNullOrEmpty(inquilinoId)) throw new AppError("inquilino_id es requerido", 400);

        var ticket = await TicketsRepository.Insertar(inquilinoId, titulo, descripcion, u.Actor);

        // Avisa al propietario en tiempo real (SSE + push) sin bloquear la respuesta.
        var ticketId = ticket.S("id")!;
        Js.Bg(async () =>
        {
            var completo = await TicketsRepository.ConPropietario(ticketId);
            var adminId = completo?.S("admin_id");
            if (completo != null && !string.IsNullOrEmpty(adminId))
            {
                SseService.EmitToUser(adminId, "ticket_new", new { ticket = completo });
                await PushService.CreateAndSendNotification(adminId, "🔔 Nuevo ticket",
                    $"{completo.S("titulo")} — Depto {completo.S("depto_numero")}", "ticket");
            }
        });
        return ticket;
    }

    public static async Task<Row> Actualizar(AuthUser u, string id, JsonObject b)
    {
        var estado = Js.T(b["estado"]) ? b.S("estado") : null;
        var nota = b.ContainsKey("nota_admin") ? b["nota_admin"] is null ? null : b.S("nota_admin") : null;
        if (!await TicketsRepository.EsDelPropietario(id, u.Id)) throw new AppError("Ticket no encontrado o no autorizado", 404);

        var actualizado = await TicketsRepository.Actualizar(id, estado, nota, u.Actor);
        SseService.EmitToUser(u.Id, "ticket_updated", new { ticket = actualizado });
        Js.Bg(async () =>
        {
            var usuarioId = await TicketsRepository.UsuarioDelInquilino(id);
            if (string.IsNullOrEmpty(usuarioId)) return;
            SseService.EmitToUser(usuarioId, "ticket_updated", new { ticket = actualizado });
            var etiqueta = estado == "en_revision" ? "en revisión" : estado == "resuelto" ? "resuelto" : "actualizado";
            await PushService.CreateAndSendNotification(usuarioId, "📋 Ticket actualizado", $"Tu reporte fue marcado como {etiqueta}", "ticket");
        });
        return actualizado;
    }

    public static async Task Eliminar(AuthUser u, string id)
    {
        if (u.Rol is "admin" or "cobrador")
        {
            if (!await TicketsRepository.EliminarDelPropietario(id, u.Id)) throw new AppError("Ticket no encontrado o no autorizado", 404);
            return;
        }
        var inquilinoId = await TicketsRepository.InquilinoActivoDelUsuario(u.Id) ?? throw new AppError("No autorizado", 403);
        if (!await TicketsRepository.EliminarRecienCreado(id, inquilinoId))
            throw new AppError("No puedes eliminar este ticket (ya pasaron 5 minutos o no es tuyo)", 403);
    }
}
