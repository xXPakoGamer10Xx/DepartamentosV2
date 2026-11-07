using System.Collections.Concurrent;
using System.Diagnostics;

namespace VertexRent.Api;

/// <summary>Convierte .docx → PDF con LibreOffice headless: salida idéntica al Word (tablas, logos, fuentes).</summary>
public static class LibreOfficeService
{
    // Dos conversiones a la vez como máximo, cada una con su propio perfil (LibreOffice bloquea el perfil).
    static readonly SemaphoreSlim Gate = new(2);
    static readonly ConcurrentQueue<int> Perfiles = new(new[] { 0, 1 });
    static string? _ruta; static bool _buscado;

    public static string? Ruta
    {
        get
        {
            if (_buscado) return _ruta;
            _buscado = true;
            var candidatos = new[]
            {
                Env.Get("SOFFICE_PATH"), "/usr/bin/soffice", "/usr/local/bin/soffice", "/opt/homebrew/bin/soffice",
                "/Applications/LibreOffice.app/Contents/MacOS/soffice",
            };
            _ruta = candidatos.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c));
            return _ruta;
        }
    }

    public static bool Disponible => Ruta != null;

    public static async Task<byte[]> DocxAPdf(byte[] docx)
    {
        var soffice = Ruta ?? throw new AppError("LibreOffice no está disponible en el servidor", 500);
        await Gate.WaitAsync();
        Perfiles.TryDequeue(out var perfil);
        var dir = Path.Combine(Path.GetTempPath(), "vr-lo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var entrada = Path.Combine(dir, "contrato.docx");
            await File.WriteAllBytesAsync(entrada, docx);
            var perfilDir = Path.Combine(Path.GetTempPath(), $"vr-lo-perfil-{perfil}");

            var psi = new ProcessStartInfo(soffice)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            psi.ArgumentList.Add($"-env:UserInstallation=file://{perfilDir}");
            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("--norestore");
            psi.ArgumentList.Add("--nolockcheck");
            psi.ArgumentList.Add("--convert-to");
            psi.ArgumentList.Add("pdf");
            psi.ArgumentList.Add("--outdir");
            psi.ArgumentList.Add(dir);
            psi.ArgumentList.Add(entrada);
            psi.Environment["HOME"] = perfilDir;

            using var proc = Process.Start(psi) ?? throw new AppError("No se pudo iniciar LibreOffice", 500);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var stderr = proc.StandardError.ReadToEndAsync();
            var stdout = proc.StandardOutput.ReadToEndAsync();
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { }
                throw new AppError("La conversión a PDF tardó demasiado", 504);
            }

            var salida = Path.Combine(dir, "contrato.pdf");
            if (!File.Exists(salida))
            {
                Console.Error.WriteLine($"LibreOffice falló: {await stderr} {await stdout}");
                throw new AppError("No se pudo convertir el contrato a PDF", 500);
            }
            return await File.ReadAllBytesAsync(salida);
        }
        finally
        {
            Perfiles.Enqueue(perfil);
            Gate.Release();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
