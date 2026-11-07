using Npgsql;
using Testcontainers.PostgreSql;

namespace VertexRent.Api.Tests;

/// <summary>Usan su propia base desechable (no el contenedor compartido); corren en serie con el resto.</summary>
[Collection("api")]
public sealed class MigracionesTests
{
    static async Task<(PostgreSqlContainer Db, string Conn)> Contenedor()
    {
        var db = new PostgreSqlBuilder().WithImage("postgres:16-alpine").WithDatabase("m").WithUsername("t").WithPassword("t").Build();
        await db.StartAsync();
        return (db, db.GetConnectionString());
    }

    static async Task<long> Cuenta(string conn, string sql)
    {
        await using var c = new NpgsqlConnection(conn); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    static void Apuntar(PostgreSqlContainer db)
    {
        Environment.SetEnvironmentVariable("DB_HOST", db.Hostname);
        Environment.SetEnvironmentVariable("DB_PORT", db.GetMappedPublicPort(5432).ToString());
        Environment.SetEnvironmentVariable("DB_NAME", "m"); Environment.SetEnvironmentVariable("DB_USER", "t"); Environment.SetEnvironmentVariable("DB_PASSWORD", "t");
        Db.Init();
    }

    static async Task Restaurar(string host, string port, string name, string user, string pass)
    {
        Environment.SetEnvironmentVariable("DB_HOST", host); Environment.SetEnvironmentVariable("DB_PORT", port);
        Environment.SetEnvironmentVariable("DB_NAME", name); Environment.SetEnvironmentVariable("DB_USER", user); Environment.SetEnvironmentVariable("DB_PASSWORD", pass);
        Db.Init();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Base_vacia_aplica_todo_y_reaplicar_no_hace_nada()
    {
        var (h, p, n, u, w) = (Environment.GetEnvironmentVariable("DB_HOST")!, Environment.GetEnvironmentVariable("DB_PORT")!, Environment.GetEnvironmentVariable("DB_NAME")!, Environment.GetEnvironmentVariable("DB_USER")!, Environment.GetEnvironmentVariable("DB_PASSWORD")!);
        var (db, conn) = await Contenedor();
        try
        {
            Apuntar(db);
            await Migrations.Apply();
            Assert.Equal(Migrations.Archivos().Count, await Cuenta(conn, "SELECT COUNT(*) FROM schema_migrations"));
            Assert.Equal(1, await Cuenta(conn, "SELECT COUNT(*) FROM information_schema.columns WHERE table_name='usuarios' AND column_name='token_version'"));
            await Migrations.Apply(); // idempotente
            Assert.Equal(Migrations.Archivos().Count, await Cuenta(conn, "SELECT COUNT(*) FROM schema_migrations"));
        }
        finally { await Restaurar(h, p, n, u, w); await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Base_anterior_a_las_migraciones_conserva_sus_datos()
    {
        var (h, p, n, u, w) = (Environment.GetEnvironmentVariable("DB_HOST")!, Environment.GetEnvironmentVariable("DB_PORT")!, Environment.GetEnvironmentVariable("DB_NAME")!, Environment.GetEnvironmentVariable("DB_USER")!, Environment.GetEnvironmentVariable("DB_PASSWORD")!);
        var (db, conn) = await Contenedor();
        try
        {
            Apuntar(db);
            // Simula producción antes de este cambio: esquema completo ya creado, sin tabla schema_migrations y con datos.
            var archivos = Migrations.Archivos();
            await using (var c = new NpgsqlConnection(conn))
            {
                await c.OpenAsync();
                foreach (var (_, ruta) in archivos)
                    await using (var cmd = new NpgsqlCommand(File.ReadAllText(ruta), c)) await cmd.ExecuteNonQueryAsync();
                await using var ins = new NpgsqlCommand("INSERT INTO usuarios (email, password_hash, nombre_completo, rol) VALUES ('viejo@x.test','h','Viejo','admin')", c);
                await ins.ExecuteNonQueryAsync();
            }
            Assert.Equal(0, await Cuenta(conn, "SELECT COUNT(*) FROM pg_tables WHERE tablename='schema_migrations'"));

            await Migrations.Apply();

            Assert.Equal(1, await Cuenta(conn, "SELECT COUNT(*) FROM usuarios WHERE email='viejo@x.test'")); // sin pérdida de datos
            Assert.Equal(archivos.Count, await Cuenta(conn, "SELECT COUNT(*) FROM schema_migrations"));
        }
        finally { await Restaurar(h, p, n, u, w); await db.DisposeAsync(); }
    }
}
