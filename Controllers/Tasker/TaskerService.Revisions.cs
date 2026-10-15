using System.Text.RegularExpressions;
namespace z3nDash;

public sealed partial class TaskerService
{
    private readonly SemaphoreSlim _mutations = new(1, 1);
    public async Task<bool> HandleRequest(TaskerExchange context)
    {
        var path=context.Request.Url.AbsolutePath;
        var mutation = context.Request.HttpMethod != "GET" && path is "/tasker/save" or "/tasker/payload" or "/tasker/delete";
        if (mutation) await _mutations.WaitAsync();
        try { return await HandleCore(context); }
        finally { if (mutation) _mutations.Release(); }
    }
    private async Task<bool> CheckRevision(TaskerExchange ctx, System.Text.Json.JsonElement body, Db db, string id)
    {
        ValidateId(id);
        if (!body.TryGetProperty("_revision", out var expected)) return true;
        var current = db.Get("edit_version", Table, where: $"\"id\" = '{id}'");
        if (expected.GetString() == _databaseEpoch + ":" + current) return true;
        ctx.Response.StatusCode = 409;
        await WriteJson(ctx.Response, new { error = "Task changed. Reload before saving.", code = "revision_conflict" });
        return false;
    }
    private string _databaseEpoch = Guid.NewGuid().ToString("N");
    public string DatabaseEpoch => _databaseEpoch;
    public static void ValidateId(string id)
    { if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,128}$")) throw new ArgumentException("Invalid task ID"); }
    private async Task Detail(TaskerExchange ctx, Db db)
    {
        var id = ctx.Request.QueryString["id"] ?? ""; ValidateId(id);
        var fields = db.GetTableColumns(Table).Where(x => x != "last_output").ToList();
        // Read each free-text field independently: the legacy row protocol has separators.
        if (string.IsNullOrEmpty(db.Get("id", Table, where: $"\"id\" = '{id}'")))
        { ctx.Response.StatusCode = 404; await WriteJson(ctx.Response, new { error = "Task not found" }); return; }
        var data = fields.ToDictionary(x => x, x => db.Get(x, Table, where: $"\"id\" = '{id}'") ?? "");
        data["_revision"] = _databaseEpoch + ":" + data.GetValueOrDefault("edit_version", "1");
        await WriteJson(ctx.Response, data);
    }
}
