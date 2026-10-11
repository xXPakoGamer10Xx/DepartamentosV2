using System.Text.Json.Nodes;

namespace VertexRent.Api;

public static class UsuariosEndpoints
{
    const string ScopeSql = @"(admin_id = $1 OR id IN (SELECT usado_por FROM codigos_invitacion WHERE admin_id = $1 AND usado_por IS NOT NULL))";

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/usuarios").Auth();

        g.MapGet("/", H(GetUsuarios)).AdminOnly();
        g.MapPost("/", H(CreateUsuario)).AdminOnly();
        g.MapPut("/{id}/toggle", H(Toggle)).AdminOnly();
        g.MapPatch("/perfil", H(UpdatePerfil));
        g.MapPatch("/{id}/rol", H(CambiarRol)).AdminOnly();
        g.MapPatch("/{id}/permisos", H(ActualizarPermisos)).AdminOnly();
        g.MapDelete("/{id}", H(Delete)).AdminOnly();
    }

    // GET /api/usuarios — el propio admin + los que invitó
    static async Task<IResult> GetUsuarios(HttpContext c)
    {
        var r = await Db.Query(
            @"SELECT u.id, u.email, u.nombre_completo, u.rol, u.avatar_url, u.activo, u.ultimo_acceso, u.created_at, u.permisos, u.rol_label
              FROM usuarios u
              WHERE u.id = $1
                 OR u.admin_id = $1
                 OR u.id IN (
                   SELECT usado_por FROM codigos_invitacion WHERE admin_id = $1 AND usado_por IS NOT NULL
                 )
              ORDER BY u.created_at ASC", c.Me().Id);
        return Api.Data(r.Rows);
    }

    static async Task<IResult> CreateUsuario(HttpContext c)
    {
        var b = await Api.Body(c);
        var email = b.S("email"); var password = b.S("password"); var nombre = b.S("nombre_completo");
        var rol = b.S("rol"); var rolLabel = b.S("rol_label");

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(nombre))
            throw new AppError("Email, contraseña y nombre son requeridos", 400);
        if (password.Length < 8) throw new AppError("La contraseña debe tener al menos 8 caracteres", 400);
        if (rol is not ("admin" or "cobrador" or "inquilino"))
            throw new AppError("Rol inválido. Usa \"admin\", \"cobrador\" o \"inquilino\"", 400);

        var existing = await Db.Query("SELECT id FROM usuarios WHERE email = $1", email.ToLowerInvariant());
        if (existing.First != null) throw new AppError("Ya existe un usuario con ese email", 409);

        var hash = BCrypt.Net.BCrypt.HashPassword(password, 12);
        var permisos = rol == "cobrador" ? Permisos.Sanitize(b["permisos"]) : new List<string>();
        var r = await Db.Query(
            @"INSERT INTO usuarios (email, password_hash, nombre_completo, rol, admin_id, permisos, rol_label)
              VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7)
              RETURNING id, email, nombre_completo, rol, activo, created_at, permisos, rol_label",
            email.ToLowerInvariant(), hash, Js.TitleCase(nombre), rol,
            rol == "admin" ? null : c.Me().Id, JsonX.Arr(permisos).ToJsonString(),
            rol == "cobrador" && !string.IsNullOrEmpty(rolLabel) ? rolLabel : null);
        return Api.Data(r.First, 201);
    }

    static async Task<IResult> Toggle(HttpContext c)
    {
        var id = Api.Route(c, "id");
        var adminId = c.Me().Id;
        if (id == adminId) throw new AppError("No puedes desactivar tu propia cuenta", 400);

        var scope = await Db.Query(
            @"SELECT 1 FROM usuarios WHERE id = $2 AND (admin_id = $1
                OR id IN (SELECT usado_por FROM codigos_invitacion WHERE admin_id = $1 AND usado_por IS NOT NULL))",
            adminId, id);
        if (scope.First == null) throw new AppError("No tienes permiso para modificar este usuario", 403);

        var r = await Db.Query("UPDATE usuarios SET activo = NOT activo WHERE id = $1 RETURNING id, email, activo", id);
        if (r.First == null) throw new AppError("Usuario no encontrado", 404);
        return Api.Data(r.First);
    }

    static async Task<IResult> CambiarRol(HttpContext c)
    {
        var id = Api.Route(c, "id");
        var b = await Api.Body(c);
        var rol = b.S("rol");
        if (rol is not ("admin" or "cobrador" or "inquilino"))
            throw new AppError("Rol inválido. Usa \"admin\", \"cobrador\" o \"inquilino\"", 400);
        if (id == c.Me().Id) throw new AppError("No puedes cambiar tu propio rol", 400);

        var r = await Db.Query(
            @"UPDATE usuarios SET rol = $1 WHERE id = $2
              RETURNING id, email, nombre_completo, rol, activo", rol, id);
        if (r.First == null) throw new AppError("Usuario no encontrado", 404);
        return Api.Data(r.First);
    }

    static async Task<IResult> UpdatePerfil(HttpContext c)
    {
        var b = await Api.Body(c);
        var nombre = b.S("nombre_completo"); var avatar = b.S("avatar_url");
        var r = await Db.Query(
            @"UPDATE usuarios SET
                nombre_completo = COALESCE($1, nombre_completo),
                avatar_url = COALESCE($2, avatar_url)
              WHERE id = $3
              RETURNING id, email, nombre_completo, rol, avatar_url",
            !string.IsNullOrEmpty(nombre) ? Js.TitleCase(nombre) : null,
            !string.IsNullOrEmpty(avatar) ? avatar : null, c.Me().Actor);
        return Api.Data(r.First);
    }

    static async Task<IResult> ActualizarPermisos(HttpContext c)
    {
        var id = Api.Route(c, "id");
        var adminId = c.Me().Id;
        if (id == adminId) throw new AppError("El administrador tiene todos los permisos", 400);

        var scope = await Db.Query(
            @"SELECT rol FROM usuarios WHERE id = $1 AND (admin_id = $2
                OR id IN (SELECT usado_por FROM codigos_invitacion WHERE admin_id = $2 AND usado_por IS NOT NULL))",
            id, adminId);
        if (scope.First == null) throw new AppError("No tienes permiso para modificar este usuario", 403);
        if (scope.First.S("rol") != "cobrador") throw new AppError("Solo los colaboradores tienen permisos configurables", 400);

        var b = await Api.Body(c);
        var permisos = Permisos.Sanitize(b["permisos"]);
        string? rolLabel = b["rol_label"] is JsonValue lv && lv.TryGetValue<string>(out var ls) ? ls : null;
        var r = await Db.Query(
            @"UPDATE usuarios SET permisos = $1::jsonb, rol_label = $4, admin_id = COALESCE(admin_id, $3) WHERE id = $2
              RETURNING id, email, nombre_completo, rol, activo, permisos, rol_label",
            JsonX.Arr(permisos).ToJsonString(), id, adminId, rolLabel);
        return Api.Data(r.First);
    }

    static async Task<IResult> Delete(HttpContext c)
    {
        var id = Api.Route(c, "id");
        var adminId = c.Me().Id;
        if (id == adminId) throw new AppError("No puedes eliminar tu propia cuenta", 400);

        var scope = await Db.Query(
            @"SELECT 1 FROM usuarios WHERE id = $2 AND (admin_id = $1
                OR id IN (SELECT usado_por FROM codigos_invitacion WHERE admin_id = $1 AND usado_por IS NOT NULL))",
            adminId, id);
        if (scope.First == null) throw new AppError("No tienes permiso para eliminar este usuario", 403);

        var r = await Db.Query("DELETE FROM usuarios WHERE id = $1 RETURNING id", id);
        if (r.First == null) throw new AppError("Usuario no encontrado", 404);
        return Api.Msg("Usuario eliminado exitosamente");
    }
}
