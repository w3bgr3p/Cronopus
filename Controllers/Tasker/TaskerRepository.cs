using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Npgsql;
namespace z3nDash;

public sealed partial class TaskerService
{
    private DbConnection OpenTaskConnection()
    {
        var db = _dbService.GetDb();
        DbConnection connection = db.Mode == dbMode.SQLite
            ? new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.Source }.ToString()) : new NpgsqlConnection(db.Source);
        connection.Open(); return connection;
    }
    public object Page(int limit = 100, string? cursor = null, string search = "")
    {
        limit = Math.Clamp(limit, 1, 500);
        var offset = 0;
        if (cursor != null)
        {
            var parts = cursor.Split(':');
            if (parts.Length != 2 || parts[0] != _databaseEpoch || !int.TryParse(parts[1], out offset) || offset < 0)
                throw new ArgumentException("List changed. Reload the first page.");
        }
        using var connection = OpenTaskConnection(); using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id,name,executor,script_path,enabled,status,last_run,last_exit,runs_total,runs_success,schedule_mode,schedule_tag,max_threads,schedule_paused,deferred_until,edit_version FROM schedules WHERE name LIKE @search ORDER BY name,id LIMIT @limit OFFSET @offset";
        foreach (var pair in new Dictionary<string, object> { ["@search"] = "%" + search + "%", ["@limit"] = limit + 1, ["@offset"] = offset })
        { var p = cmd.CreateParameter(); p.ParameterName = pair.Key; p.Value = pair.Value; cmd.Parameters.Add(p); }
        var items = new List<Dictionary<string, string>>(); using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new Dictionary<string, string>();
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString()!;
            row["_revision"] = _databaseEpoch + ":" + row["edit_version"]; items.Add(row);
        }
        var more = items.Count > limit; if (more) items.RemoveAt(limit);
        return new { items, next_cursor = more ? _databaseEpoch + ":" + (offset + limit) : null, database_epoch = _databaseEpoch };
    }
}
