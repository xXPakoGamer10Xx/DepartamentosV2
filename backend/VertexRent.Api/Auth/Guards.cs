using System.Text.Json.Nodes;

namespace VertexRent.Api;

public static class Guards
{
    static string? BearerToken(HttpContext c)
    {
        var h = c.Request.Headers.Authorization.ToString();
        return h.StartsWith("Bearer ") ? h.Split(' ')[1] : null;
    }

    static string? BearerOrQuery(HttpContext c)
    {
        var raw = BearerToken(c);
        if (raw != null) return raw;
        if (!c.Request.Headers.Authorization.ToString().StartsWith("Bearer ")) 
        {
            var q = c.Request.Query["token"].ToString();
            return string.IsNullOrEmpty(q) ? null : q;
        }
        return null;
    }

    static AuthUser FromPayload(JsonObject p)
    {
        var u = new AuthUser
        {
            Id = Js.Str(p["id"]) ?? "",
            Email = Js.Str(p["email"]) ?? "",
            Rol = Js.Str(p["rol"]) ?? "",
            NombreCompleto = Js.Str(p["nombre_completo"]) ?? "",
            AdminId = p["admin_id"] is null ? null : Js.Str(p["admin_id"]),
            Raw = p,
        };
        return u;
    }

    // ── authMiddleware ────────────────────────────────────────────────────────
    public static TB Auth<TB>(this TB b) where TB : IEndpointConventionBuilder => b.AddEndpointFilter(async (ctx, next) =>
    {
        var c = ctx.HttpContext;
        var raw = BearerToken(c);
        if (raw == null) throw new AppError("Token de autenticación requerido", 401);
        var payload = Jwt.Verify(raw) ?? throw new AppError("Token inválido o expirado", 401);
        var user = FromPayload(payload);
        // Un colaborador opera SOBRE los datos de su admin: id se reescribe al del admin.
        if (user.Rol == "cobrador" && !string.IsNullOrEmpty(user.AdminId))
        {
            user.ActorId = user.Id;
            user.Id = user.AdminId!;
        }
        c.Items["__user"] = user;
        return await next(ctx);
    });

    public static TB AdminOnly<TB>(this TB b) where TB : IEndpointConventionBuilder => b.AddEndpointFilter(async (ctx, next) =>
    {
        if (ctx.HttpContext.Me().Rol != "admin") throw new AppError("Acceso restringido a administradores", 403);
        return await next(ctx);
    });

    public static TB CobradorOrAdmin<TB>(this TB b) where TB : IEndpointConventionBuilder => b.AddEndpointFilter(async (ctx, next) =>
    {
        var rol = ctx.HttpContext.Me().Rol;
        if (rol != "admin" && rol != "cobrador") throw new AppError("Acceso restringido a administradores y cobradores", 403);
        return await next(ctx);
    });

    /// <summary>Admin pasa siempre; cobrador necesita ALGUNO de los permisos (se leen frescos de la BD).</summary>
    public static async Task EnsurePermiso(HttpContext c, params string[] keys)
    {
        var u = c.Me();
        if (u.Rol == "admin") return;
        if (u.Rol != "cobrador") throw new AppError("Acceso restringido", 403);
        var r = await Db.Query("SELECT permisos FROM usuarios WHERE id = $1 AND activo = TRUE", u.ActorId ?? u.Id);
        var arr = r.First?.J("permisos") as JsonArray;
        var permisos = arr?.Select(x => Js.Str(x)).Where(x => x != null).ToHashSet() ?? new HashSet<string?>();
        if (keys.Any(k => permisos.Contains(k))) return;
        throw new AppError("No tienes permiso para esta acción", 403);
    }

    public static TB Permiso<TB>(this TB b, params string[] keys) where TB : IEndpointConventionBuilder => b.AddEndpointFilter(async (ctx, next) =>
    {
        await EnsurePermiso(ctx.HttpContext, keys);
        return await next(ctx);
    });

    /// <summary>Tickets: el inquilino siempre pasa; el resto necesita el permiso 'tickets'.</summary>
    public static TB InquilinoOrPermiso<TB>(this TB b, string permiso) where TB : IEndpointConventionBuilder => b.AddEndpointFilter(async (ctx, next) =>
    {
        if (ctx.HttpContext.Me().Rol != "inquilino") await EnsurePermiso(ctx.HttpContext, permiso);
        return await next(ctx);
    });

    // ── pdfAuth: token de un solo propósito (scope pdf_download) ─────────────
    public static TB PdfAuth<TB>(this TB b) where TB : IEndpointConventionBuilder => b.AddEndpointFilter(async (ctx, next) =>
    {
        var c = ctx.HttpContext;
        var raw = BearerOrQuery(c) ?? throw new AppError("Token de autenticación requerido", 401);
        var p = Jwt.Verify(raw) ?? throw new AppError("Token inválido o expirado", 401);
        if (Js.Str(p["scope"]) != "pdf_download" || Js.Str(p["inquilino_id"]) != Api.Route(c, "id"))
            throw new AppError("Token inválido para este recurso", 403);
        c.Items["__user"] = new AuthUser { Id = Js.Str(p["admin_id"]) ?? "", Rol = "admin" };
        return await next(ctx);
    });

    // ── sseAuth: JWT de sesión por header o ?token= ──────────────────────────
    public static TB SseAuth<TB>(this TB b) where TB : IEndpointConventionBuilder => b.AddEndpointFilter(async (ctx, next) =>
    {
        var c = ctx.HttpContext;
        var raw = BearerOrQuery(c) ?? throw new AppError("Token de autenticación requerido", 401);
        var p = Jwt.Verify(raw) ?? throw new AppError("Token inválido o expirado", 401);
        c.Items["__user"] = FromPayload(p);
        return await next(ctx);
    });

    // ── softAuth: opcional ───────────────────────────────────────────────────
    public static TB SoftAuth<TB>(this TB b) where TB : IEndpointConventionBuilder => b.AddEndpointFilter(async (ctx, next) =>
    {
        var c = ctx.HttpContext;
        var raw = BearerOrQuery(c);
        if (raw != null)
        {
            var p = Jwt.Verify(raw);
            if (p != null) c.Items["__user"] = FromPayload(p);
        }
        return await next(ctx);
    });
}
