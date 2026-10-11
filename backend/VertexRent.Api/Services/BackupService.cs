using System.Diagnostics;
using Cronos;

namespace VertexRent.Api;

public static class BackupService
{
    // pg_dump local: en Docker viene instalado; en desarrollo puede estar en Homebrew o Postgres.app.
    static string? BuscarPgDump()
    {
        var candidatos = new List<string?> { Env.Get("PGDUMP_PATH") };
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) candidatos.Add(Path.Combine(d, "pg_dump"));
        candidatos.AddRange(new[]
        {
            "/opt/homebrew/opt/libpq/bin/pg_dump", "/opt/homebrew/opt/postgresql@16/bin/pg_dump", "/usr/local/opt/libpq/bin/pg_dump",
            "/usr/local/opt/postgresql@16/bin/pg_dump", "/Applications/Postgres.app/Contents/Versions/latest/bin/pg_dump",
        });
        return candidatos.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c));
    }

    static async Task Dump(string filepath)
    {
        var user = Env.Get("DB_USER", "depas_user"); var db = Env.Get("DB_NAME", "departamentos");
        var local = BuscarPgDump();

        ProcessStartInfo psi;
        if (local != null)
        {
            psi = new ProcessStartInfo(local) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var a in new[] { "-h", Env.Get("DB_HOST", "localhost"), "-p", Env.Get("DB_PORT", "5432"), "-U", user, "-d", db, "-F", "p", "-f", filepath })
                psi.ArgumentList.Add(a);
            psi.Environment["PGPASSWORD"] = Env.Get("DB_PASSWORD", "");
        }
        else
        {
            // Sin pg_dump en esta máquina (típico en desarrollo): se usa el del contenedor de la base de datos.
            var contenedor = Env.Get("DB_CONTAINER", "depas_db");
            psi = new ProcessStartInfo("docker") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var a in new[] { "exec", "-e", $"PGPASSWORD={Env.Get("DB_PASSWORD", "")}", contenedor, "pg_dump", "-U", user, "-d", db, "-F", "p" })
                psi.ArgumentList.Add(a);
        }

        Process proc;
        try { proc = Process.Start(psi) ?? throw new Exception("sin proceso"); }
        catch (Exception) when (local == null)
        {
            throw new AppError("No se encontró pg_dump ni Docker para hacer el respaldo. En desarrollo instala las herramientas de Postgres (brew install libpq) o levanta la base de datos con Docker.", 500);
        }
        using (proc)
        {
            // Con Docker el volcado llega por la salida estándar; con pg_dump local ya se escribe directo al archivo (-f).
            var salida = local == null ? File.Create(filepath) : null;
            var copia = salida != null ? proc.StandardOutput.BaseStream.CopyToAsync(salida) : proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await copia; salida?.Dispose();
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0)
            {
                try { File.Delete(filepath); } catch { }
                throw new Exception($"pg_dump falló ({proc.ExitCode}): {stderr}");
            }
        }
    }

    public static async Task<(string archivo, long tamano)> RunBackup()
    {
        var dir = Env.Get("BACKUP_DIR", "./backups");
        Directory.CreateDirectory(dir);

        var ts = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH-mm-ss-fff'Z'");
        var filename = $"backup_{ts}.sql";
        var filepath = Path.Combine(dir, filename);

        await Dump(filepath);

        var size = new FileInfo(filepath).Length;
        await Db.Query(
            "INSERT INTO backups (archivo, tamano_bytes, tipo, estado) VALUES ($1, $2, 'automatico', 'completado')",
            filename, size);

        // Limpiar backups con más de 30 días
        var cutoff = DateTime.Now.AddDays(-30);
        foreach (var f in Directory.GetFiles(dir, "*.sql"))
            if (File.GetLastWriteTime(f) < cutoff) File.Delete(f);

        Console.WriteLine($"✅ Backup completado: {filename} ({size} bytes)");
        return (filename, size);
    }
}

/// <summary>Backup automático según BACKUP_CRON (por defecto 4:00 AM, hora CDMX).</summary>
public sealed class BackupCronService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var expr = Env.Get("BACKUP_CRON", "0 4 * * *");
        CronExpression cron;
        try { cron = CronExpression.Parse(expr); }
        catch (Exception ex) { Console.Error.WriteLine($"❌ BACKUP_CRON inválido ({expr}): {ex.Message}"); return; }
        Console.WriteLine($"⏰ Backup automático programado: {expr} (Hora CDMX)");

        while (!ct.IsCancellationRequested)
        {
            var next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, Js.Cdmx);
            if (next == null) return;
            var delay = next.Value - DateTimeOffset.UtcNow;
            try { await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, ct); }
            catch (OperationCanceledException) { return; }

            Console.WriteLine($"🗄️  Iniciando backup automático ({DateTime.UtcNow:O})...");
            try { await BackupService.RunBackup(); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Error en backup automático: {ex.Message}");
                try
                {
                    await Db.Query(
                        "INSERT INTO backups (archivo, tamano_bytes, tipo, estado) VALUES ($1, 0, 'automatico', 'fallido')",
                        $"backup_fallido_{DateTime.UtcNow:O}.sql");
                }
                catch { }
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }
}
