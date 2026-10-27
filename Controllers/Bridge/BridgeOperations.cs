using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Cronopus;

public sealed class BridgeOperations
{
    private readonly string _source;
    private readonly object _gate = new();
    public BridgeOperations(string root)
    {
        _source = new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "operations.db") }.ToString();
        using var connection = Open(); using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS operations(key TEXT,id TEXT,hash TEXT,state TEXT,status INTEGER,text TEXT,type TEXT,created TEXT,PRIMARY KEY(key,id)); UPDATE operations SET state='outcome_unknown' WHERE state='pending'; DELETE FROM operations WHERE created < datetime('now','-7 days')";
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var c = new SqliteConnection(_source); c.Open(); return c; }
    public TaskerReply Start(string key,string id,string input,Func<Task<TaskerReply>> action)
    {
        var running=ExecuteAsync(key,id,input,()=>Task.Run(action));
        if(running.IsCompletedSuccessfully)return running.Result;
        _=running.ContinueWith(t=>Console.WriteLine("Bridge operation journal failed"),TaskContinuationOptions.OnlyOnFaulted);
        return new(202,JsonSerializer.Serialize(new{operation_id=id,state="pending"}),"application/json");
    }
    public object? Get(string key, string id)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT state,status,text FROM operations WHERE key=$key AND id=$id";
        cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader(); return r.Read() ? new { request_id = id, state = r.GetString(0), status = r.GetInt32(1), result = r.GetString(2) } : null;
    }
    public void SetProgress(string key,string id,string text)
    {
        if(text.Length>65536)text=text[^65536..];
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="UPDATE operations SET text=$text WHERE key=$key AND id=$id AND state='pending'";
        cmd.Parameters.AddWithValue("$text",text);cmd.Parameters.AddWithValue("$key",key);cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();
    }
    public async Task<TaskerReply> ExecuteAsync(string key, string id, string input, Func<Task<TaskerReply>> action)
    {
        if (id.Length is < 1 or > 128) throw new ArgumentException("X-Request-ID required");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        lock (_gate)
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT hash,state,status,text,type FROM operations WHERE key=$key AND id=$id";
            cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            if (r.Read()) return r.GetString(0) != hash ? new(409, "{\"error\":\"Request ID reused with different input\"}", "application/json")
                : r.GetString(1) is "pending" or "outcome_unknown" ? new(202, JsonSerializer.Serialize(Get(key, id)), "application/json")
                : new(r.GetInt32(2), r.GetString(3), r.GetString(4));
            r.Close(); cmd.CommandText = "DELETE FROM operations WHERE state!='pending' AND created < datetime('now','-7 days')";cmd.ExecuteNonQuery();
            cmd.CommandText = "SELECT COUNT(*) FROM operations";
            if ((long)cmd.ExecuteScalar()! >= 100000) return new(503, "{\"error\":\"Operation journal full\"}", "application/json");
            cmd.CommandText = "INSERT INTO operations VALUES($key,$id,$hash,'pending',202,'','application/json',datetime('now'))";
            cmd.Parameters.AddWithValue("$hash", hash); cmd.ExecuteNonQuery();
        }
        TaskerReply result;
        try { result = await action(); }
        catch (Exception ex) { result = new(500, JsonSerializer.Serialize(new { error = ex is ArgumentException ? ex.Message : "Operation failed" }), "application/json"); }
        using (var c = Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE operations SET state=$state,status=$status,text=$text,type=$type WHERE key=$key AND id=$id";
            cmd.Parameters.AddWithValue("$state", result.StatusCode < 400 ? "succeeded" : "failed");
            cmd.Parameters.AddWithValue("$status", result.StatusCode); cmd.Parameters.AddWithValue("$text", result.Text);
            cmd.Parameters.AddWithValue("$type", result.ContentType); cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        return result;
    }
}
