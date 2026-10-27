using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Npgsql;
namespace Cronopus;

public sealed partial class TaskerService
{
    private DbConnection OpenTaskConnection()
    {
        var db = _dbService.GetDb();
        DbConnection connection = db.Mode == dbMode.SQLite
            ? new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.Source }.ToString()) : new NpgsqlConnection(db.Source);
        connection.Open(); return connection;
    }
    public string SavedOutputTail(string id)
    {
        ValidateId(id);using var connection=OpenTaskConnection();using var cmd=connection.CreateCommand();
        cmd.CommandText="SELECT substr(last_output,CASE WHEN length(last_output)>32768 THEN length(last_output)-32767 ELSE 1 END,32768) FROM schedules WHERE id=@id";
        var parameter=cmd.CreateParameter();parameter.ParameterName="@id";parameter.Value=id;cmd.Parameters.Add(parameter);
        return cmd.ExecuteScalar()?.ToString()??"";
    }
    public object Page(int limit = 100, string? cursor = null, string search = "")
    {
        limit = Math.Clamp(limit, 1, 500);
        string? afterName=null,afterId=null;
        if (cursor != null)
        {
            try{var parts=JsonSerializer.Deserialize<string[]>(Convert.FromBase64String(cursor))!;
                if(parts.Length!=4||parts[0]!=_databaseEpoch||parts[3]!=search)throw new ArgumentException("List changed. Reload the first page.");afterName=parts[1];afterId=parts[2];
            }catch(FormatException){throw new ArgumentException("Invalid page cursor");}
        }
        using var connection = OpenTaskConnection(); using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id,name,executor,script_path,enabled,status,last_run,last_exit,runs_total,runs_success,schedule_mode,schedule_tag,max_threads,schedule_paused,deferred_until,edit_version FROM schedules WHERE name LIKE @search"+(afterName==null?"":" AND (name>@name OR (name=@name AND id>@id))")+" ORDER BY name,id LIMIT @limit";
        var parameters=new Dictionary<string,object>{["@search"]="%"+search+"%",["@limit"]=limit+1};
        if(afterName!=null){parameters["@name"]=afterName;parameters["@id"]=afterId!;}
        foreach (var pair in parameters)
        { var p = cmd.CreateParameter(); p.ParameterName = pair.Key; p.Value = pair.Value; cmd.Parameters.Add(p); }
        var items = new List<Dictionary<string, string>>(); using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new Dictionary<string, string>();
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString()!;
            row["_revision"] = _databaseEpoch + ":" + row["edit_version"]; items.Add(row);
        }
        var more = items.Count > limit; if (more) items.RemoveAt(limit);
        return new { items, next_cursor = more ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new[]{_databaseEpoch,items[^1]["name"],items[^1]["id"],search})) : null, database_epoch = _databaseEpoch };
    }
}
