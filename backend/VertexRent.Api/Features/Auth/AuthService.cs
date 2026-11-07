using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Inicio de sesión, registro y contraseñas. El SQL vive en <see cref="AuthRepository"/>.</summary>
public static class AuthService
{
    const string CodigoInvalido = "Código de invitación inválido, expirado o ya utilizado";

    public static async Task<Dictionary<string, object?>> Login(string email, string password)
    {
        var user = await AuthRepository.UsuarioParaLogin(email.ToLowerInvariant().Trim());
        if (user == null || !user.B("activo")) throw new AppError("Credenciales inválidas", 401);
        if (!BCrypt.Net.BCrypt.Verify(password, user.S("password_hash"))) throw new AppError("Credenciales inválidas", 401);

        await AuthRepository.RegistrarAcceso(user.S("id")!);

        var permisos = user.J("permisos") as JsonArray ?? new JsonArray();
        var payload = new JsonObject
        {
            ["id"] = user.S("id"), ["email"] = user.S("email"),
            ["rol"] = user.S("rol"), ["nombre_completo"] = user.S("nombre_completo"),
            ["tv"] = user.I("token_version"),
        };
        if (user.S("rol") != "admin")
        {
            payload["admin_id"] = user.S("admin_id");
            payload["permisos"] = permisos.DeepClone();
        }
        return new()
        {
            ["token"] = Jwt.Create(payload),
            ["user"] = new Dictionary<string, object?>
            {
                ["id"] = user.S("id"), ["email"] = user.S("email"),
                ["nombre_completo"] = user.S("nombre_completo"), ["rol"] = user.S("rol"),
                ["avatar_url"] = user.S("avatar_url"),
                ["permisos"] = user.S("rol") == "admin" ? null : permisos,
            },
        };
    }

    public static async Task<Row> Me(AuthUser u)
    {
        var row = await AuthRepository.Perfil(u.Actor) ?? throw new AppError("Usuario no encontrado", 404);
        row["permisos"] = row.S("rol") == "admin" ? null : (row.J("permisos") is JsonArray a ? a : new JsonArray());
        row["plataforma"] = row.S("rol") == "admin" && Env.IsPlatformAdmin(row.S("email"));
        return row;
    }

    public static async Task<Dictionary<string, object?>> Registrar(string email, string password, string nombre, string? rol, string? inviteCode)
    {
        var correo = email.ToLowerInvariant().Trim();
        if (password.Length < 8) throw new AppError("La contraseña debe tener al menos 8 caracteres", 400);
        var userRol = rol == "admin" ? "admin" : "inquilino";
        // No se puede registrar un alias del correo del dueño de la plataforma (mismo mensaje que un duplicado).
        if (Env.IsPlatformAlias(email)) throw new AppError("Ya existe una cuenta con ese correo electrónico", 409);

        // Registro abierto (servicio): cualquiera puede crear su propia cuenta de administrador.
        // Si ADMIN_SIGNUP_CODE está definido, se vuelve a exigir (modo instalación privada).
        if (userRol == "admin")
        {
            var codigoPlataforma = Env.Get("ADMIN_SIGNUP_CODE");
            if (!string.IsNullOrEmpty(codigoPlataforma) && inviteCode != codigoPlataforma)
                throw new AppError("Código de administrador inválido", 403);
        }

        string? tokenInquilino = null, codigoId = null, adminId = null, rolLabel = null;
        JsonNode? codigoPermisos = null;

        if (userRol != "admin")
        {
            if (string.IsNullOrEmpty(inviteCode)) throw new AppError("Se requiere un código de invitación válido", 403);

            if (await AuthRepository.CodigoVigente(inviteCode) is { } codigo)       // 1) sistema nuevo: codigos_invitacion
            {
                userRol = codigo.S("rol")!; codigoId = codigo.S("id"); adminId = codigo.S("admin_id");
                codigoPermisos = codigo.J("permisos"); rolLabel = codigo.S("rol_label");
            }
            else                                                                       // 2) sistema anterior: invitation_token
            {
                // invitation_token es UUID: un código con otro formato (p. ej. ya usado) no puede ser uno.
                var inq = (Guid.TryParse(inviteCode, out _) ? await AuthRepository.InquilinoPorTokenAntiguo(inviteCode) : null)
                    ?? throw new AppError(CodigoInvalido, 403);
                var correoInvitado = inq.S("email_invitacion");
                if (!string.IsNullOrEmpty(correoInvitado) && correoInvitado.ToLowerInvariant() != correo)
                    throw new AppError("El correo debe coincidir con la invitación original", 403);
                userRol = "inquilino"; tokenInquilino = inviteCode; adminId = inq.S("admin_id");
            }
        }

        // Despliegue de un solo admin: si el código no trae admin_id, usar el único admin.
        if (userRol != "admin" && string.IsNullOrEmpty(adminId)) adminId = await AuthRepository.PrimerAdmin();

        if (await AuthRepository.ExisteEmail(correo)) throw new AppError("Ya existe una cuenta con ese correo electrónico", 409);

        var hash = BCrypt.Net.BCrypt.HashPassword(password, 12);
        var permisos = userRol == "cobrador" ? Permisos.Sanitize(codigoPermisos) : new List<string>();

        // Todo en una transacción: el código se consume de forma atómica (no se puede usar dos veces en paralelo)
        // y, si algo falla, no queda ni el usuario a medias ni el código quemado.
        var user = await Db.Tx(async tx =>
        {
            if (codigoId != null && !await AuthRepository.ConsumirCodigo(tx, codigoId)) throw new AppError(CodigoInvalido, 403);
            var nuevo = await AuthRepository.InsertarUsuario(tx, correo, hash, Js.TitleCase(nombre), userRol,
                userRol == "admin" ? null : adminId, JsonX.Arr(permisos).ToJsonString(), userRol == "cobrador" ? rolLabel : null);
            if (codigoId != null) await AuthRepository.MarcarCodigoUsadoPor(tx, codigoId, nuevo.S("id")!);
            else if (tokenInquilino != null && !await AuthRepository.VincularInquilinoPorToken(tx, nuevo.S("id")!, tokenInquilino))
                throw new AppError(CodigoInvalido, 403);
            return nuevo;
        });

        var payload = new JsonObject
        {
            ["id"] = user.S("id"), ["email"] = user.S("email"),
            ["rol"] = user.S("rol"), ["nombre_completo"] = user.S("nombre_completo"),
        };
        if (user.S("rol") != "admin")
        {
            payload["admin_id"] = adminId;
            payload["permisos"] = JsonX.Arr(permisos);
        }
        user["permisos"] = user.S("rol") == "cobrador" ? JsonX.Arr(permisos) : null;
        return new() { ["token"] = Jwt.Create(payload), ["user"] = user };
    }

    /// <summary>Cambia la contraseña de quien inició sesión (no la del admin dueño) y devuelve un token nuevo para esta sesión.</summary>
    public static async Task<string> CambiarPassword(AuthUser u, string actual, string nueva)
    {
        if (nueva.Length < 8) throw new AppError("La contraseña nueva debe tener al menos 8 caracteres", 400);
        var hash = await AuthRepository.HashDePassword(u.Actor);
        if (hash == null || !BCrypt.Net.BCrypt.Verify(actual, hash)) throw new AppError("Contraseña actual incorrecta", 400);

        var version = await AuthRepository.CambiarPassword(u.Actor, BCrypt.Net.BCrypt.HashPassword(nueva, 12));
        var payload = new JsonObject((u.Raw ?? new JsonObject()).Select(kv => KeyValuePair.Create(kv.Key, kv.Value?.DeepClone())))
        {
            ["tv"] = version,
        };
        return Jwt.Create(payload);
    }
}
