namespace VertexRent.Api;

/// <summary>
/// Migraciones versionadas: archivos <c>Sql/migrations/NNNN_nombre.sql</c> que se aplican una sola vez, en orden,
/// cada una en su propia transacción y con un bloqueo (varias instancias no se pisan).
/// Para cambiar el esquema se AÑADE un archivo nuevo; los ya aplicados no se editan.
/// </summary>
public static class Migrations
{
    const long LockId = 7272740001;
    const string BaseVersion = "0001_esquema_base";

    static string Dir => Path.Combine(AppContext.BaseDirectory, "Sql", "migrations");

    public static IReadOnlyList<(string Version, string Path)> Archivos() =>
        Directory.GetFiles(Dir, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(f => (System.IO.Path.GetFileNameWithoutExtension(f), f)).ToList();

    /// <summary>Aplica las migraciones pendientes. Seguro de ejecutar en cada arranque.</summary>
    public static async Task Apply()
    {
        await Db.Query("SELECT 1");
        foreach (var (version, path) in Archivos())
        {
            var sql = await File.ReadAllTextAsync(path);
            var aplicada = await Db.Tx(async tx =>
            {
                await tx.Query("SELECT pg_advisory_xact_lock($1::bigint)", LockId);
                await tx.Query("CREATE TABLE IF NOT EXISTS schema_migrations (version TEXT PRIMARY KEY, aplicada_en TIMESTAMPTZ NOT NULL DEFAULT NOW())");
                if ((await tx.Query("SELECT 1 FROM schema_migrations WHERE version = $1", version)).First != null) return false;

                // Base de datos anterior a las migraciones versionadas: el esquema base ya existe, solo se registra.
                if (version == BaseVersion
                    && (await tx.Query("SELECT to_regclass('public.inquilinos') IS NOT NULL AS existe")).First!.B("existe"))
                {
                    await tx.Query("INSERT INTO schema_migrations (version) VALUES ($1)", version);
                    Console.WriteLine($"📌 Migración {version}: base existente, registrada como aplicada");
                    return false;
                }

                await tx.Query(sql);
                await tx.Query("INSERT INTO schema_migrations (version) VALUES ($1)", version);
                return true;
            });
            if (aplicada) Console.WriteLine($"✅ Migración aplicada: {version}");
        }
    }

    /// <summary>
    /// DESTRUCTIVO (solo desarrollo): borra TODO el esquema público y lo recrea con las migraciones.
    /// </summary>
    public static async Task Reset()
    {
        Console.WriteLine("🔄 Reiniciando la base de datos...");
        await Db.Query("DROP SCHEMA public CASCADE; CREATE SCHEMA public;");
        await Apply();
        Console.WriteLine("✅ Esquema recreado");
    }
}
