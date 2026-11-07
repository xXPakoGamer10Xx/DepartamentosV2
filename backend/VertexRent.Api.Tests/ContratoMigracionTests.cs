using Npgsql;

namespace VertexRent.Api.Tests;

/// <summary>Migración única del modelo anterior de contratos (HTML global y por inquilino) a plantillas Word.</summary>
[Collection("api")]
public sealed class ContratoMigracionTests(ApiFixture api)
{
    async Task<long> Cuenta(string sql, params (string, object)[] ps)
    {
        await using var c = new NpgsqlConnection(api.ConnectionString); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    async Task Ejecutar(string sql, params (string, object?)[] ps)
    {
        await using var c = new NpgsqlConnection(api.ConnectionString); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Migra_la_plantilla_global_y_los_contratos_por_inquilino_una_sola_vez()
    {
        var a = await api.RegisterAdmin();
        var inq = await api.CreateInquilino(a, 1);
        var admin = Guid.Parse(a.Id); var inqId = Guid.Parse(inq);
        // Estado del modelo anterior: plantilla HTML global en la configuración y un contrato HTML editado para el inquilino
        await Ejecutar("INSERT INTO configuracion (admin_id, clave, valor) VALUES (@a, 'contrato_html_template', '<p>Contrato global {{inquilino_nombre}}</p>')", ("a", admin));
        await Ejecutar("UPDATE inquilinos SET contrato_html = '<p>Contrato solo para este inquilino</p>' WHERE id = @i", ("i", inqId));

        await ContratoMigracion.Run();

        Assert.Equal(1, await Cuenta("SELECT COUNT(*) FROM plantillas_contrato WHERE admin_id = @a AND nombre = 'Mi plantilla anterior' AND estado = 'lista' AND predeterminada", ("a", admin)));
        Assert.Equal(1, await Cuenta("SELECT COUNT(*) FROM plantillas_contrato WHERE admin_id = @a AND nombre LIKE 'Personalizado –%' AND estado = 'lista' AND NOT predeterminada", ("a", admin)));
        Assert.Equal(0, await Cuenta("SELECT COUNT(*) FROM configuracion WHERE admin_id = @a AND clave = 'contrato_html_template'", ("a", admin)));   // el origen se borra
        Assert.Equal(1, await Cuenta("SELECT COUNT(*) FROM inquilinos WHERE id = @i AND contrato_html IS NULL AND plantilla_id IS NOT NULL", ("i", inqId)));

        await ContratoMigracion.Run();   // repetir el arranque no duplica nada
        Assert.Equal(2, await Cuenta("SELECT COUNT(*) FROM plantillas_contrato WHERE admin_id = @a", ("a", admin)));
    }

    [Fact]
    public async Task Sin_datos_anteriores_no_hace_nada()
    {
        var a = await api.RegisterAdmin();
        await ContratoMigracion.Run();
        Assert.Equal(0, await Cuenta("SELECT COUNT(*) FROM plantillas_contrato WHERE admin_id = @a", ("a", Guid.Parse(a.Id))));
    }
}
