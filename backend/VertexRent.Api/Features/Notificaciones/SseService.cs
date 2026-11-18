using System.Collections.Concurrent;
using System.Text.Json;

namespace VertexRent.Api;

public sealed class SseClient
{
    public required HttpResponse Res { get; init; }
    /// <summary>Propietario del equipo al que pertenece quien conecta (él mismo, su admin o el admin de su inquilino).</summary>
    public string? OwnerId { get; init; }
    readonly SemaphoreSlim _lock = new(1, 1);
    public volatile bool Dead;

    public async Task<bool> Write(string text)
    {
        if (Dead) return false;
        await _lock.WaitAsync();
        try
        {
            await Res.WriteAsync(text);
            await Res.Body.FlushAsync();
            return true;
        }
        catch { Dead = true; return false; }
        finally { _lock.Release(); }
    }
}

public static class SseService
{
    static readonly ConcurrentDictionary<string, List<SseClient>> Clients = new();
    static readonly ConcurrentDictionary<string, byte> AdminIds = new();
    static readonly ConcurrentDictionary<string, List<SseClient>> Teams = new();

    public static void Add(string userId, string rol, SseClient c)
    {
        var list = Clients.GetOrAdd(userId, _ => new List<SseClient>());
        lock (list) list.Add(c);
        if (rol == "admin") AdminIds[userId] = 0;
        if (!string.IsNullOrEmpty(c.OwnerId))
        {
            var team = Teams.GetOrAdd(c.OwnerId, _ => new List<SseClient>());
            lock (team) team.Add(c);
        }
    }

    public static void Remove(string userId, SseClient c)
    {
        if (!string.IsNullOrEmpty(c.OwnerId) && Teams.TryGetValue(c.OwnerId, out var team))
        {
            lock (team) { team.Remove(c); if (team.Count == 0) Teams.TryRemove(c.OwnerId, out _); }
        }
        if (!Clients.TryGetValue(userId, out var list)) return;
        lock (list)
        {
            list.Remove(c);
            if (list.Count == 0)
            {
                Clients.TryRemove(userId, out _);
                AdminIds.TryRemove(userId, out _);
            }
        }
    }

    public static void EmitToUser(string userId, string evt, object data)
    {
        if (!Clients.TryGetValue(userId, out var list)) return;
        var payload = $"event: {evt}\ndata: {JsonSerializer.Serialize(data, Api.JsonOpts)}\n\n";
        SseClient[] snapshot;
        lock (list) snapshot = list.ToArray();
        foreach (var c in snapshot)
            _ = Task.Run(async () =>
            {
                if (!await c.Write(payload)) Remove(userId, c);
            });
    }

    /// <summary>Avisa a todos los dispositivos del equipo (propietario, colaboradores e inquilinos) de que hubo cambios.</summary>
    public static void EmitToTeam(string ownerId, string evt, object data)
    {
        if (!Teams.TryGetValue(ownerId, out var list)) return;
        var payload = $"event: {evt}\ndata: {JsonSerializer.Serialize(data, Api.JsonOpts)}\n\n";
        SseClient[] snapshot;
        lock (list) snapshot = list.ToArray();
        foreach (var c in snapshot) _ = Task.Run(async () => { await c.Write(payload); });
    }

    public static void EmitToAdmins(string evt, object data)
    {
        foreach (var id in AdminIds.Keys.ToArray()) EmitToUser(id, evt, data);
    }
}
