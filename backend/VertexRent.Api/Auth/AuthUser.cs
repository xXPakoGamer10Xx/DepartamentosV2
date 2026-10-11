using System.Text.Json.Nodes;

namespace VertexRent.Api;

public sealed class AuthUser
{
    public string Id { get; set; } = "";           // para un cobrador se reescribe al id del admin dueño
    public string Email { get; set; } = "";
    public string Rol { get; set; } = "";
    public string NombreCompleto { get; set; } = "";
    public string? AdminId { get; set; }
    public string? ActorId { get; set; }
    public JsonObject? Raw { get; set; }

    /// <summary>Id del admin dueño de los datos de la petición actual.</summary>
    public string OwnerId => Rol == "admin" ? Id : (AdminId ?? Id);
    /// <summary>Id del usuario real que actúa (auditoría, creado_por).</summary>
    public string Actor => ActorId ?? Id;
}

public static class HttpContextExtensions
{
    public static AuthUser? MaybeMe(this HttpContext c) => c.Items.TryGetValue("__user", out var u) ? u as AuthUser : null;
    public static AuthUser Me(this HttpContext c) => c.MaybeMe() ?? throw new AppError("Token de autenticación requerido", 401);
}
