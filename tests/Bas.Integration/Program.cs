using System.Text.Json;
using Cronopus;

var root = Path.Combine(Path.GetTempPath(), "cronopus-bas-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    if (args.Contains("--live")) { await LiveCases.Run(root); return; }
    XmlCases.Run(root);
    ContextCases.Run(root);
    StorageCases.Run(root);
    EngineFilesCases.Run(root);
    var source = TestProject.Create(root);
    var store = new BridgeStore(root); var fake = new FakeEngine();
    await using var service = new BasControlService(store, () => fake);
    var saved = service.Save(new BasProjectEdit { Name = "XML", ProjectPath = source });
    try { service.Save(new BasProjectEdit { Id = saved.Id, Revision = "stale", Name = "bad" }); throw new Exception("stale edit accepted"); }
    catch (BasConflictException) { }
    service.Save(new BasProjectEdit { Id = saved.Id, Revision = saved.Revision, Name = "XML", ProjectPath = source });
    var runs = service.Run(saved.Id, 1);
    await fake.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    try { service.Run(saved.Id, 1); throw new Exception("concurrent XML execution accepted"); } catch (BasConflictException) { }
    await service.StopAsync(saved.Id, runs[0].Id);
    if (service.Runs(saved.Id).Single().Status != "stopped") throw new Exception("stop failed");
    service.Run(saved.Id, 1); fake.Complete.TrySetResult(JsonSerializer.SerializeToElement(new { successes = 1 }));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (service.Runs(saved.Id)[0].Status != "succeeded") await Task.Delay(20, timeout.Token);
    try { await service.DeleteAsync(saved.Id, saved.Revision); throw new Exception("stale delete accepted"); } catch (BasConflictException) { }
    await using var reopened = new BasControlService(new BridgeStore(root), () => new FakeEngine());
    if (reopened.List().Single().ProjectPath != source) throw new Exception("project persistence failed");
    Console.WriteLine("PASS XML project persistence, revisions, concurrent-run guard, stop and execution result");
    await UnloadCases.Run(root);
    await DeleteCases.Run(root);
    await PackageCases.Run(root);
    await ApiCases.Run(root, args.Contains("--serve"));
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    // Windows can briefly retain native browser DLL mappings after the job closes.
    for (var attempt = 0; ; attempt++)
    {
        try { Directory.Delete(root, true); break; }
        catch (Exception error) when (attempt < 20 && error is IOException or UnauthorizedAccessException)
        { await Task.Delay(100); }
    }
}

sealed class FakeEngine : IBasControlEngine
{
    public event Action<string, JsonElement>? Message;
    public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<JsonElement> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task StartAsync(BasProject project, CancellationToken ct) { Started.TrySetResult(); return Task.CompletedTask; }
    public Task<JsonElement> RunAsync(JsonElement parameters, int threadId, CancellationToken ct) => Complete.Task.WaitAsync(ct);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

static class TestProject
{
    public static string Create(string root)
    {
        Directory.CreateDirectory(root); var file = Path.Combine(root, "test.xml");
        File.WriteAllText(file, "<BrowserAutomationStudioProject><EngineVersion>31.1.0</EngineVersion><Script><![CDATA[section(1,1,1,0,function(){})!]]></Script><ModelList><Model><Name>X</Name><Type>FixedInteger</Type><Value>2</Value></Model><Model><Name>Y</Name><Type>FixedInteger</Type><Value>3</Value></Model></ModelList></BrowserAutomationStudioProject>");
        return file;
    }
}
