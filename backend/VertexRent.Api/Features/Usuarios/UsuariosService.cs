using System.Text.Json.Nodes;

namespace VertexRent.Api;

/// <summary>Gestión de usuarios del propietario. El SQL vive en <see cref="UsuariosRepository"/>.</summary>
public static class UsuariosService
{
    static async Task<Row> EnAlcance(AuthUser u, string id, string mensajeSinPermiso) =>
        await UsuariosRepository.EnAlcance(u.Id, id) ?? throw new AppError(mensajeSinPermiso, 403);

    public static Task<List<Row>> Listar(AuthUser u) => UsuariosRepository.Listar(u.Id);

    public static async Task<Row> Crear(AuthUser u, JsonObject b)
    {
        var email = b.S("email"); var password = b.S("password"); var nombre = b.S("nombre_completo");
        var rol = b.S("rol"); var rolLabel = b.S("rol_label");
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(nombre))
            throw new AppError("Email, contraseña y nombre son requeridos", 400);
        if (password.Length < 8) throw new AppError("La contraseña debe tener al menos 8 caracteres", 400);
        if (rol is not ("admin" or "cobrador" or "inquilino"))
            throw new AppError("Rol inválido. Usa \"admin\", \"cobrador\" o \"inquilino\"", 400);

        var correo = email.ToLowerInvariant();
        if (await UsuariosRepository.ExisteEmail(correo)) throw new AppError("Ya existe un usuario con ese email", 409);

        var permisos = rol == "cobrador" ? Permisos.Sanitize(b["permisos"]) : new List<string>();
        return await UsuariosRepository.Insertar(correo, BCrypt.Net.BCrypt.HashPassword(password, 12), Js.TitleCase(nombre), rol,
            rol == "admin" ? null : u.Id, JsonX.Arr(permisos).ToJsonString(),
            rol == "cobrador" && !string.IsNullOrEmpty(rolLabel) ? rolLabel : null);
    }

    public static async Task<Row> AlternarActivo(AuthUser u, string id)
    {
        if (id == u.Id) throw new AppError("No puedes desactivar tu propia cuenta", 400);
        await EnAlcance(u, id, "No tienes permiso para modificar este usuario");
        return await UsuariosRepository.AlternarActivo(id) ?? throw new AppError("Usuario no encontrado", 404);
    }

    public static async Task<Row> CambiarRol(AuthUser u, string id, string? rol)
    {
        if (rol is not ("cobrador" or "inquilino")) throw new AppError("Rol inválido. Usa \"cobrador\" o \"inquilino\"", 400);
        if (id == u.Id) throw new AppError("No puedes cambiar tu propio rol", 400);
        var destino = await EnAlcance(u, id, "No tienes permiso para modificar este usuario");
        if (destino.S("rol") == "admin") throw new AppError("No se puede cambiar el rol de un administrador", 400);
        return await UsuariosRepository.CambiarRol(id, rol, u.Id) ?? throw new AppError("Usuario no encontrado", 404);
    }

    /// <summary>Perfil propio (de quien inició sesión, no del admin dueño).</summary>
    public static async Task<Row?> ActualizarPerfil(AuthUser u, JsonObject b)
    {
        var nombre = b.S("nombre_completo");
        var avatar = Inputs.Imagen(b["avatar_url"], "avatar_url");
        return await UsuariosRepository.ActualizarPerfil(u.Actor,
            !string.IsNullOrEmpty(nombre) ? Js.TitleCase(nombre) : null, !string.IsNullOrEmpty(avatar) ? avatar : null);
    }

    public static async Task<Row?> ActualizarPermisos(AuthUser u, string id, JsonObject b)
    {
        if (id == u.Id) throw new AppError("El administrador tiene todos los permisos", 400);
        var destino = await EnAlcance(u, id, "No tienes permiso para modificar este usuario");
        if (destino.S("rol") != "cobrador") throw new AppError("Solo los colaboradores tienen permisos configurables", 400);

        var permisos = Permisos.Sanitize(b["permisos"]);
        string? rolLabel = b["rol_label"] is JsonValue lv && lv.TryGetValue<string>(out var ls) ? ls : null;
        return await UsuariosRepository.ActualizarPermisos(id, JsonX.Arr(permisos).ToJsonString(), rolLabel, u.Id);
    }

    public static async Task Eliminar(AuthUser u, string id)
    {
        if (id == u.Id) throw new AppError("No puedes eliminar tu propia cuenta", 400);
        await EnAlcance(u, id, "No tienes permiso para eliminar este usuario");
        if (!await UsuariosRepository.Eliminar(id)) throw new AppError("Usuario no encontrado", 404);
    }
}
