using System.Collections.Concurrent;
using System.Text.Json;

namespace VertexRent.Api;

public sealed class SseClient
{
    public required HttpResponse Res { get; init; }
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

    public static void Add(string userId, string rol, SseClient c)
    {
        var list = Clients.GetOrAdd(userId, _ => new List<SseClient>());
        lock (list) list.Add(c);
        if (rol == "admin") AdminIds[userId] = 0;
    }

    public static void Remove(string userId, SseClient c)
    {
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

    public static void EmitToAdmins(string evt, object data)
    {
        foreach (var id in AdminIds.Keys.ToArray()) EmitToUser(id, evt, data);
    }
}
