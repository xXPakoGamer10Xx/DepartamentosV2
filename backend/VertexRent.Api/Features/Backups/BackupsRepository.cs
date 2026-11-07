namespace VertexRent.Api;

/// <summary>Todo el SQL del registro de respaldos. Sin reglas de negocio ni HTTP.</summary>
internal static class BackupsRepository
{
    public static async Task<List<Row>> Recientes() =>
        (await Db.Query("SELECT * FROM backups ORDER BY created_at DESC LIMIT 50")).Rows;

    public static Task Registrar(string archivo, long tamanoBytes, string estado) =>
        Db.Query("INSERT INTO backups (archivo, tamano_bytes, tipo, estado) VALUES ($1, $2, 'automatico', $3)", archivo, tamanoBytes, estado);

    public static Task MarcarManual(string archivo) =>
        Db.Query("UPDATE backups SET tipo = 'manual' WHERE archivo = $1", archivo);
}
