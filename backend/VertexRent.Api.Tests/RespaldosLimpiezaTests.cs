namespace VertexRent.Api.Tests;

public sealed class RespaldosLimpiezaTests : IDisposable
{
    readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "vr-resp-" + Guid.NewGuid().ToString("N"))).FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    string Crear(string nombre, int diasDeAntiguedad)
    {
        var ruta = Path.Combine(_dir, nombre);
        File.WriteAllText(ruta, "-- respaldo");
        File.SetLastWriteTime(ruta, DateTime.Now.AddDays(-diasDeAntiguedad));
        return ruta;
    }

    [Fact]
    public void Borra_solo_los_respaldos_generados_con_mas_de_30_dias()
    {
        var viejoGenerado = Crear("backup_2026-09-01T04-00-00-123Z.sql", 60);
        var recienteGenerado = Crear("backup_2026-11-06T04-00-00-123Z.sql", 1);
        var manualViejo = Crear("manual_pre_claude_20260907_204324.sql", 60);
        var manualConPrefijo = Crear("backup_manual_pre_ajuste_sep_2026.sql", 40);

        BackupService.LimpiarAntiguos(_dir, DateTime.Now);

        Assert.False(File.Exists(viejoGenerado));      // generado y antiguo: se limpia
        Assert.True(File.Exists(recienteGenerado));    // generado y reciente: se queda
        Assert.True(File.Exists(manualViejo));         // puesto a mano: nunca se toca
        Assert.True(File.Exists(manualConPrefijo));    // aunque empiece con "backup_", si no tiene el formato de la app, se conserva
    }

    [Fact]
    public void Carpeta_vacia_no_falla() => BackupService.LimpiarAntiguos(_dir, DateTime.Now);
}
