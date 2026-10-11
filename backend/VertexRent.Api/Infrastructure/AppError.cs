namespace VertexRent.Api;

public class AppError : Exception
{
    public int StatusCode { get; }
    public AppError(string message, int statusCode = 500) : base(message) { StatusCode = statusCode; }
}
