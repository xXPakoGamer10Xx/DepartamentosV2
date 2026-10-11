using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace VertexRent.Api;

/// <summary>Fila de resultado. Los valores siguen el formato que produce node-pg para que el JSON sea idéntico.</summary>
public class Row : Dictionary<string, object?>
{
    public string? S(string k) => TryGetValue(k, out var v) ? v switch { null => null, string s => s, JsonNode n => n.ToJsonString(), _ => Convert.ToString(v, CultureInfo.InvariantCulture) } : null;
    public decimal D(string k) => decimal.TryParse(S(k), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    public int I(string k) => (int)Math.Round(D(k));
    public bool B(string k) => TryGetValue(k, out var v) && v is bool b && b;
    public JsonNode? J(string k) => TryGetValue(k, out var v) ? v as JsonNode : null;
    public Row Copy() { var r = new Row(); foreach (var kv in this) r[kv.Key] = kv.Value; return r; }
    public Row With(params (string, object?)[] extra) { var r = Copy(); foreach (var (k, v) in extra) r[k] = v; return r; }
    public Row Without(string key) { var r = Copy(); r.Remove(key); return r; }
    public DateTime? Dt(string k) => DateTime.TryParse(S(k), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}

public sealed class QueryResult
{
    public List<Row> Rows { get; } = new();
    public int RowCount { get; set; }
    public Row? First => Rows.Count > 0 ? Rows[0] : null;
}

public static class Db
{
    static NpgsqlDataSource? _ds;
    static readonly TimeZoneInfo LocalTz = TimeZoneInfo.Local;

    public static void Init()
    {
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = Env.Get("DB_HOST", "localhost"),
            Port = int.TryParse(Env.Get("DB_PORT"), out var p) ? p : 5432,
            Database = Env.Get("DB_NAME", "departamentos"),
            Username = Env.Get("DB_USER", "depas_user"),
            Password = Env.Get("DB_PASSWORD", "depas_secret_2024"),
            MaxPoolSize = 20,
            Timeout = 15,
        };
        _ds = NpgsqlDataSource.Create(csb.ConnectionString);
    }

    public static NpgsqlConnectionStringBuilder ConnString() => new(_ds!.ConnectionString);

    public static async Task<QueryResult> Query(string sql, params object?[] args)
    {
        await using var conn = await _ds!.OpenConnectionAsync();
        return await Run(conn, null, sql, args);
    }

    /// <summary>Ejecuta varias consultas en una sola transacción (equivalente a withTransaction).</summary>
    public static async Task<T> Tx<T>(Func<DbTx, Task<T>> fn)
    {
        await using var conn = await _ds!.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            var r = await fn(new DbTx(conn, tx));
            await tx.CommitAsync();
            return r;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    internal static async Task<QueryResult> Run(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, object?[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var a in args) cmd.Parameters.Add(ToParam(a));
        var result = new QueryResult();
        await using var reader = await cmd.ExecuteReaderAsync();
        var n = reader.FieldCount;
        var names = new string[n];
        var types = new string[n];
        for (var i = 0; i < n; i++) { names[i] = reader.GetName(i); types[i] = reader.GetDataTypeName(i); }
        while (await reader.ReadAsync())
        {
            var row = new Row();
            for (var i = 0; i < n; i++)
                row[names[i]] = reader.IsDBNull(i) ? null : Convert(reader, i, types[i]);
            result.Rows.Add(row);
        }
        result.RowCount = reader.RecordsAffected >= 0 && n == 0 ? reader.RecordsAffected : result.Rows.Count;
        return result;
    }

    static object? Convert(NpgsqlDataReader r, int i, string type)
    {
        switch (type.ToLowerInvariant())
        {
            case "smallint": return (int)r.GetInt16(i);
            case "integer": return r.GetInt32(i);
            case "bigint": return r.GetInt64(i).ToString(CultureInfo.InvariantCulture); // node-pg: string
            case "numeric": return r.GetDecimal(i).ToString(CultureInfo.InvariantCulture); // node-pg: string
            case "double precision": return r.GetDouble(i);
            case "real": return (double)r.GetFloat(i);
            case "boolean": return r.GetBoolean(i);
            case "uuid": return r.GetGuid(i).ToString();
            case "bytea": return r.GetFieldValue<byte[]>(i);
            case "json":
            case "jsonb":
                return JsonNode.Parse(r.GetString(i));
            case "date":
            {
                var d = r.GetFieldValue<DateOnly>(i);
                var local = DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
                return Iso(TimeZoneInfo.ConvertTimeToUtc(local, LocalTz));
            }
            case "timestamp with time zone":
                return Iso(r.GetFieldValue<DateTime>(i).ToUniversalTime());
            case "timestamp without time zone":
            {
                var local = DateTime.SpecifyKind(r.GetFieldValue<DateTime>(i), DateTimeKind.Unspecified);
                return Iso(TimeZoneInfo.ConvertTimeToUtc(local, LocalTz));
            }
            default:
                var v = r.GetValue(i);
                return v is string s ? s : System.Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }

    public static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    static NpgsqlParameter ToParam(object? v)
    {
        if (v is byte[] bytes) return new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bytea, Value = bytes };
        var text = ToText(v);
        return new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Unknown, Value = (object?)text ?? DBNull.Value };
    }

    static string? ToText(object? v)
    {
        switch (v)
        {
            case null: return null;
            case string s: return s;
            case bool b: return b ? "true" : "false";
            case DateTime dt: return Iso(dt.ToUniversalTime());
            case DateTimeOffset dto: return Iso(dto.UtcDateTime);
            case JsonValue jv:
                if (jv.TryGetValue<string>(out var str)) return str;
                return jv.ToJsonString();
            case JsonNode jn: return jn.ToJsonString();
            case IEnumerable<string> arr: return ArrayLiteral(arr);
            case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
            default: return v.ToString();
        }
    }

    static string ArrayLiteral(IEnumerable<string> items)
    {
        var sb = new StringBuilder("{");
        var first = true;
        foreach (var it in items)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(it.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
        }
        return sb.Append('}').ToString();
    }
}

public sealed class DbTx
{
    readonly NpgsqlConnection _c;
    readonly NpgsqlTransaction _t;
    internal DbTx(NpgsqlConnection c, NpgsqlTransaction t) { _c = c; _t = t; }
    public Task<QueryResult> Query(string sql, params object?[] args) => Db.Run(_c, _t, sql, args);
}
