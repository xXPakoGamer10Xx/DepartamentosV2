using System.Text.Json;

namespace VertexRent.Api;

public static class Audit
{
    /// <summary>Registra en la tabla auditoria; un fallo aquí nunca interrumpe el flujo.</summary>
    public static async Task Log(HttpContext c, string tabla, string accion, string registroId, object? previos, object? nuevos)
    {
        try
        {
            var u = c.MaybeMe();
            await Db.Query(
                @"INSERT INTO auditoria (usuario_id, tabla_afectada, accion, registro_id, datos_anteriores, datos_nuevos, ip_address)
                  VALUES ($1, $2, $3, $4, $5, $6, $7)",
                u == null ? null : u.Actor, tabla, accion, registroId,
                previos == null ? null : JsonSerializer.Serialize(previos, Api.JsonOpts),
                nuevos == null ? null : JsonSerializer.Serialize(nuevos, Api.JsonOpts),
                Api.Ip(c));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error en auditoría: {ex.Message}");
        }
    }
}
