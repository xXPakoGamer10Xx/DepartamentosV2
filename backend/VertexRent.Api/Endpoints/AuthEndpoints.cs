using System.Text.Json.Nodes;

namespace VertexRent.Api;

public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapPost("/login", H(Login)).Limit(RateLimits.Auth, "auth");
        g.MapPost("/register", H(Register)).Limit(RateLimits.Auth, "auth");
        g.MapGet("/me", H(Me)).Auth();
        g.MapPost("/change-password", H(ChangePassword)).Auth();
    }

    static async Task<IResult> Login(HttpContext c)
    {
        var b = await Api.Body(c);
        var v = new Validator(b).Email("email").Str("password", min: 1, msg: "Contraseña requerida");
        if (v.Result is { } bad) return bad;

        var email = b.S("email");
        var password = b.S("password");
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            throw new AppError("Email y contraseña son requeridos", 400);

        var result = await Db.Query(
            @"SELECT id, email, password_hash, nombre_completo, rol, avatar_url, activo, admin_id, permisos
              FROM usuarios WHERE email = $1", email.ToLowerInvariant().Trim());

        var user = result.First;
        if (user == null || !user.B("activo")) throw new AppError("Credenciales inválidas", 401);

        if (!BCrypt.Net.BCrypt.Verify(password, user.S("password_hash")))
            throw new AppError("Credenciales inválidas", 401);

        await Db.Query("UPDATE usuarios SET ultimo_acceso = NOW() WHERE id = $1", user.S("id"));

        var permisos = user.J("permisos") as JsonArray ?? new JsonArray();
        var payload = new JsonObject
        {
            ["id"] = user.S("id"), ["email"] = user.S("email"),
            ["rol"] = user.S("rol"), ["nombre_completo"] = user.S("nombre_completo"),
        };
        if (user.S("rol") != "admin")
        {
            payload["admin_id"] = user.S("admin_id");
            payload["permisos"] = permisos.DeepClone();
        }
        var token = Jwt.Create(payload);

        return Api.Data(new Dictionary<string, object?>
        {
            ["token"] = token,
            ["user"] = new Dictionary<string, object?>
            {
                ["id"] = user.S("id"), ["email"] = user.S("email"),
                ["nombre_completo"] = user.S("nombre_completo"), ["rol"] = user.S("rol"),
                ["avatar_url"] = user.S("avatar_url"),
                ["permisos"] = user.S("rol") == "admin" ? null : permisos,
            },
        });
    }

    static async Task<IResult> Me(HttpContext c)
    {
        var u = c.Me();
        var r = await Db.Query(
            @"SELECT id, email, nombre_completo, rol, avatar_url, ultimo_acceso, created_at, permisos, rol_label
              FROM usuarios WHERE id = $1", u.Actor);
        var row = r.First ?? throw new AppError("Usuario no encontrado", 404);
        row["permisos"] = row.S("rol") == "admin" ? null : (row.J("permisos") is JsonArray a ? a : new JsonArray());
        return Api.Data(row);
    }

    static async Task<IResult> Register(HttpContext c)
    {
        var b = await Api.Body(c);
        var v = new Validator(b).Email("email")
            .Str("password", min: 8, msg: "La contraseña debe tener al menos 8 caracteres")
            .Str("nombre_completo", max: 120, optional: true, trim: true);
        if (v.Result is { } bad) return bad;

        var email = b.S("email");
        var password = b.S("password");
        var nombre = b.S("nombre_completo");
        var rol = b.S("rol");
        var inviteCode = b.S("invite_code");

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(nombre))
            throw new AppError("Email, contraseña y nombre son requeridos", 400);
        if (password.Length < 8) throw new AppError("La contraseña debe tener al menos 8 caracteres", 400);

        var userRol = rol == "admin" ? "admin" : "inquilino";

        // Registro abierto (servicio): cualquiera puede crear su propia cuenta de administrador.
        // Si ADMIN_SIGNUP_CODE está definido, se vuelve a exigir (modo instalación privada).
        if (userRol == "admin")
        {
            var platformCode = Env.Get("ADMIN_SIGNUP_CODE");
            if (!string.IsNullOrEmpty(platformCode) && inviteCode != platformCode)
                throw new AppError("Código de administrador inválido", 403);
        }

        string? inquilinoTokenToLink = null;
        string? codigoInvitacionId = null;
        string? codigoAdminId = null;
        JsonNode? codigoPermisos = null;
        string? codigoRolLabel = null;

        if (userRol != "admin")
        {
            if (string.IsNullOrEmpty(inviteCode)) throw new AppError("Se requiere un código de invitación válido", 403);

            // 1) Sistema nuevo: tabla codigos_invitacion
            var codigoResult = await Db.Query(
                @"SELECT id, rol, expira_en, admin_id, permisos, rol_label FROM codigos_invitacion
                  WHERE codigo = $1 AND usado = false
                    AND (expira_en IS NULL OR expira_en > NOW())", inviteCode);

            if (codigoResult.First is { } rec)
            {
                userRol = rec.S("rol")!;
                codigoInvitacionId = rec.S("id");
                codigoAdminId = rec.S("admin_id");
                codigoPermisos = rec.J("permisos");
                codigoRolLabel = rec.S("rol_label");
            }
            else
            {
                // 2) Fallback: invitation_token de inquilinos (sistema anterior)
                // invitation_token es UUID: un código con otro formato (p. ej. ya usado) no puede ser uno.
                var tokenResult = Guid.TryParse(inviteCode, out _)
                    ? await Db.Query("SELECT id, email_invitacion, admin_id FROM inquilinos WHERE invitation_token = $1 AND usuario_id IS NULL", inviteCode)
                    : null;
                var inq = tokenResult?.First
                    ?? throw new AppError("Código de invitación inválido, expirado o ya utilizado", 403);
                var invEmail = inq.S("email_invitacion");
                if (!string.IsNullOrEmpty(invEmail) && invEmail.ToLowerInvariant() != email.ToLowerInvariant().Trim())
                    throw new AppError("El correo debe coincidir con la invitación original", 403);
                userRol = "inquilino";
                inquilinoTokenToLink = inviteCode;
                codigoAdminId = inq.S("admin_id");
            }
        }

        // Despliegue de un solo admin: si el código no trae admin_id, usar el único admin.
        if (userRol != "admin" && string.IsNullOrEmpty(codigoAdminId))
        {
            var solo = await Db.Query("SELECT id FROM usuarios WHERE rol = 'admin' ORDER BY created_at ASC LIMIT 1");
            codigoAdminId = solo.First?.S("id");
        }

        var existing = await Db.Query("SELECT id FROM usuarios WHERE email = $1", email.ToLowerInvariant().Trim());
        if (existing.First != null) throw new AppError("Ya existe una cuenta con ese correo electrónico", 409);

        var hash = BCrypt.Net.BCrypt.HashPassword(password, 12);
        var permisosFinal = userRol == "cobrador" ? Permisos.Sanitize(codigoPermisos) : new List<string>();
        var ins = await Db.Query(
            @"INSERT INTO usuarios (email, password_hash, nombre_completo, rol, admin_id, permisos, rol_label)
              VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7)
              RETURNING id, email, nombre_completo, rol, avatar_url",
            email.ToLowerInvariant().Trim(), hash, Js.TitleCase(nombre), userRol,
            userRol == "admin" ? null : codigoAdminId, JsonX.Arr(permisosFinal).ToJsonString(),
            userRol == "cobrador" ? codigoRolLabel : null);
        var user = ins.First!;

        if (codigoInvitacionId != null)
            await Db.Query("UPDATE codigos_invitacion SET usado = true, usado_en = NOW(), usado_por = $1 WHERE id = $2",
                user.S("id"), codigoInvitacionId);
        else if (inquilinoTokenToLink != null)
            await Db.Query("UPDATE inquilinos SET usuario_id = $1, invitation_token = NULL WHERE invitation_token = $2",
                user.S("id"), inquilinoTokenToLink);

        var jwtPayload = new JsonObject
        {
            ["id"] = user.S("id"), ["email"] = user.S("email"),
            ["rol"] = user.S("rol"), ["nombre_completo"] = user.S("nombre_completo"),
        };
        if (user.S("rol") != "admin")
        {
            jwtPayload["admin_id"] = codigoAdminId;
            jwtPayload["permisos"] = JsonX.Arr(permisosFinal);
        }
        var token = Jwt.Create(jwtPayload);
        user["permisos"] = user.S("rol") == "cobrador" ? JsonX.Arr(permisosFinal) : null;
        return Api.Data(new Dictionary<string, object?> { ["token"] = token, ["user"] = user }, 201);
    }

    static async Task<IResult> ChangePassword(HttpContext c)
    {
        var b = await Api.Body(c);
        var v = new Validator(b)
            .Str("passwordActual", min: 1, msg: "Contraseña actual requerida")
            .Str("passwordNueva", min: 8, msg: "La nueva contraseña debe tener al menos 8 caracteres");
        if (v.Result is { } bad) return bad;

        var actual = b.S("passwordActual");
        var nueva = b.S("passwordNueva");
        if (string.IsNullOrEmpty(actual) || string.IsNullOrEmpty(nueva))
            throw new AppError("Contraseña actual y nueva son requeridas", 400);
        if (nueva.Length < 8) throw new AppError("La contraseña nueva debe tener al menos 8 caracteres", 400);

        var u = c.Me();
        var r = await Db.Query("SELECT password_hash FROM usuarios WHERE id = $1", u.Id);
        if (r.First == null || !BCrypt.Net.BCrypt.Verify(actual, r.First.S("password_hash")))
            throw new AppError("Contraseña actual incorrecta", 400);

        await Db.Query("UPDATE usuarios SET password_hash = $1 WHERE id = $2", BCrypt.Net.BCrypt.HashPassword(nueva, 12), u.Id);
        return Api.Msg("Contraseña actualizada correctamente");
    }
}
