namespace VertexRent.Api;

public static class Migrations
{
    static string ReadSql(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", name));

    /// <summary>Migraciones incrementales idempotentes que corren en cada arranque (initDB).</summary>
    public static async Task InitDb()
    {
        await Db.Query("SELECT 1");
        await Db.Query(ReadSql("startup.sql"));
    }

    /// <summary>Equivalente a `npm run migrate:prod`: recrea el esquema completo. DESTRUCTIVO (hace DROP de las tablas).</summary>
    public static async Task RunFullMigrate()
    {
        Console.WriteLine("🔄 Ejecutando migraciones...");
        try
        {
            await Db.Query(ReadSql("migrate.sql"));
            Console.WriteLine("✅ Tablas creadas/verificadas correctamente");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Error en migración: {ex}");
            throw;
        }
    }
}
