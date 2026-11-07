namespace VertexRent.Api;

/// <summary>
/// Rutas de autenticación. La forma del cuerpo se valida aquí (<see cref="Validator"/> devuelve el 400 con la lista de
/// errores); las reglas viven en <see cref="AuthService"/>.
/// </summary>
public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapPost("/login", H(Login)).Limit(RateLimits.Auth, "auth");
        g.MapPost("/register", H(Register)).Limit(RateLimits.Auth, "auth");
        g.MapGet("/me", H(async c => Api.Data(await AuthService.Me(c.Me())))).Auth();
        g.MapPost("/change-password", H(CambiarPassword)).Auth();
    }

    static async Task<IResult> Login(HttpContext c)
    {
        var b = await Api.Body(c);
        if (new Validator(b).Email("email").Str("password", min: 1, msg: "Contraseña requerida").Result is { } bad) return bad;
        var email = b.S("email"); var password = b.S("password");
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)) throw new AppError("Email y contraseña son requeridos", 400);
        return Api.Data(await AuthService.Login(email, password));
    }

    static async Task<IResult> Register(HttpContext c)
    {
        var b = await Api.Body(c);
        var v = new Validator(b).Email("email")
            .Str("password", min: 8, msg: "La contraseña debe tener al menos 8 caracteres")
            .Str("nombre_completo", max: 120, optional: true, trim: true);
        if (v.Result is { } bad) return bad;

        var email = b.S("email"); var password = b.S("password"); var nombre = b.S("nombre_completo");
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(nombre))
            throw new AppError("Email, contraseña y nombre son requeridos", 400);
        return Api.Data(await AuthService.Registrar(email, password, nombre, b.S("rol"), b.S("invite_code")), 201);
    }

    static async Task<IResult> CambiarPassword(HttpContext c)
    {
        var b = await Api.Body(c);
        var v = new Validator(b)
            .Str("passwordActual", min: 1, msg: "Contraseña actual requerida")
            .Str("passwordNueva", min: 8, msg: "La nueva contraseña debe tener al menos 8 caracteres");
        if (v.Result is { } bad) return bad;
        var actual = b.S("passwordActual"); var nueva = b.S("passwordNueva");
        if (string.IsNullOrEmpty(actual) || string.IsNullOrEmpty(nueva)) throw new AppError("Contraseña actual y nueva son requeridas", 400);
        var token = await AuthService.CambiarPassword(c.Me(), actual, nueva);
        return Api.Data(new Dictionary<string, object?> { ["token"] = token });
    }
}
