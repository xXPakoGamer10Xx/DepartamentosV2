using System.Text.Json.Nodes;

namespace VertexRent.Api;

public static class BackupsEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/backups").Auth().AdminOnly().PlatformAdmin();

        g.MapGet("/", async () =>
            Api.Data(await BackupsRepository.Recientes()));

        g.MapPost("/manual", async () =>
        {
            var (archivo, tamano) = await BackupService.RunBackup();
            await BackupsRepository.MarcarManual(archivo);
            return Api.Json(new Dictionary<string, object?>
            {
                ["success"] = true, ["message"] = "Backup manual completado",
                ["data"] = new Dictionary<string, object?> { ["archivo"] = archivo, ["tamano_bytes"] = tamano },
            });
        });

        g.MapGet("/{archivo}/descargar", (HttpContext c) =>
        {
            // Sanitizar nombre para evitar path traversal
            var safe = Path.GetFileName(Api.Route(c, "archivo"));
            var path = Path.Combine(Env.Get("BACKUP_DIR", "./backups"), safe);
            if (!File.Exists(path)) throw new AppError("Archivo de backup no encontrado", 404);
            return Results.File(Path.GetFullPath(path), "application/sql", safe);
        });
    }
}
