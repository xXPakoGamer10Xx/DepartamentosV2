global using static VertexRent.Api.RouteHelpers;

namespace VertexRent.Api;

public static class RouteHelpers
{
    /// <summary>
    /// Fuerza el enlace como Delegate: un grupo de métodos (HttpContext) → Task&lt;IResult&gt; se enlazaría a
    /// RequestDelegate y el IResult devuelto se descartaría.
    /// </summary>
    public static Delegate H(Func<HttpContext, Task<IResult>> f) => f;
}
