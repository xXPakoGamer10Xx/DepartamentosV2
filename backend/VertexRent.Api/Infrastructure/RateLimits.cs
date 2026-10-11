using System.Collections.Concurrent;

namespace VertexRent.Api;

public sealed class LimitPolicy
{
    public required TimeSpan Window { get; init; }
    public required int Max { get; init; }
    public bool SkipSuccessful { get; init; }
    public required string Message { get; init; }
    readonly ConcurrentDictionary<string, (int count, DateTime reset)> _hits = new();

    /// <summary>Cuenta una petición. Devuelve false si excede el límite.</summary>
    public bool Hit(string key)
    {
        var now = DateTime.UtcNow;
        var e = _hits.AddOrUpdate(key,
            _ => (1, now + Window),
            (_, old) => now > old.reset ? (1, now + Window) : (old.count + 1, old.reset));
        if (_hits.Count > 5000) Sweep(now);
        return e.count <= Max;
    }

    public void Undo(string key) =>
        _hits.AddOrUpdate(key, _ => (0, DateTime.UtcNow + Window), (_, old) => (Math.Max(0, old.count - 1), old.reset));

    void Sweep(DateTime now)
    {
        foreach (var kv in _hits) if (now > kv.Value.reset) _hits.TryRemove(kv.Key, out _);
    }
}

public static class RateLimits
{
    public static readonly LimitPolicy General = new()
    {
        Window = TimeSpan.FromMinutes(15), Max = 1000,
        Message = "Demasiadas solicitudes, intenta más tarde",
    };
    // Anti fuerza bruta en login/registro; los intentos exitosos no cuentan.
    public static readonly LimitPolicy Auth = new()
    {
        Window = TimeSpan.FromMinutes(15), Max = 20, SkipSuccessful = true,
        Message = "Demasiados intentos de acceso, espera unos minutos",
    };
    // Endpoints por token público (QR de pago, invite-codes): previene enumeración.
    public static readonly LimitPolicy Token = new()
    {
        Window = TimeSpan.FromMinutes(5), Max = 60,
        Message = "Demasiadas solicitudes, intenta más tarde",
    };

    public static TB Limit<TB>(this TB b, LimitPolicy p, string name) where TB : IEndpointConventionBuilder => b.AddEndpointFilter(async (ctx, next) =>
    {
        var key = $"{name}:{Api.Ip(ctx.HttpContext)}";
        if (!p.Hit(key)) throw new AppError(p.Message, 429);
        if (!p.SkipSuccessful) return await next(ctx);
        object? result;
        try { result = await next(ctx); }
        catch { throw; } // los fallos (excepciones) sí cuentan
        var status = (result as Microsoft.AspNetCore.Http.IStatusCodeHttpResult)?.StatusCode ?? 200;
        if (status < 400) p.Undo(key);
        return result;
    });
}
