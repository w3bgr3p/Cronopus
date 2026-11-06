using System.Text.Json;
using Cronopus;

static class DeleteCases
{
    public static async Task Run(string root)
    {
        var store = new BridgeStore(Path.Combine(root, "delete"));
        var engine = new Engine();
        await using var service = new BasControlService(store, () => engine);
        var source = TestProject.Create(root);
        var project = service.Save(new BasProjectEdit { Name = "Delete stopped", ProjectPath = source });
        service.Run(project.Id, 1);
        await engine.Running.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<TaskerReply> Delete(string revision) => service.InvokeAsync("DELETE", "projects/" + project.Id,
            JsonSerializer.SerializeToElement(new { revision }));
        if ((await Delete(project.Revision)).StatusCode != 409 || engine.Disposals != 0)
            throw new Exception("Deleting an active project must be rejected without unloading it");
        await service.StopAsync(project.Id);
        if ((await Delete("stale")).StatusCode != 409 || engine.Disposals != 0)
            throw new Exception("Stale deletion must not unload the runtime");
        var deleting = Delete(project.Revision);
        try
        {
            if (deleting.IsCompleted && (await deleting).StatusCode != 200)
                throw new Exception("Stopped loaded project cannot be deleted without Release runtime");
            await engine.Disposing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try { service.Run(project.Id, 1); throw new Exception("Run admitted during deletion"); } catch (BasConflictException) { }
            try { service.Save(new BasProjectEdit { Id = project.Id, Revision = project.Revision, Name = "Race", ProjectPath = source }); throw new Exception("Edit admitted during deletion"); } catch (BasConflictException) { }
            if ((await Delete(project.Revision)).StatusCode != 409) throw new Exception("Concurrent deletion admitted");
        }
        finally { engine.Released.TrySetResult(); }
        if ((await deleting).StatusCode != 200 || service.List().Count != 0 || engine.Disposals != 1)
            throw new Exception("Stopped project deletion did not release its runtime exactly once");
        await using var reopened = new BasControlService(store);
        if (reopened.List().Count != 0 || !File.Exists(source)) throw new Exception("Deletion persistence or source preservation failed");
        Console.WriteLine("PASS stopped loaded project deletion, active/stale guards, concurrent run/edit/delete guards and source preservation");
    }
    private sealed class Engine : IBasControlEngine
    {
        public event Action<string, JsonElement>? Message;
        public readonly TaskCompletionSource Running = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Disposing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Disposals;
        public Task StartAsync(BasProject project, CancellationToken ct) => Task.CompletedTask;
        public async Task<JsonElement> RunAsync(JsonElement parameters, int threadId, CancellationToken ct)
        { Running.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return default; }
        public async ValueTask DisposeAsync()
        { Interlocked.Increment(ref Disposals); Disposing.TrySetResult(); await Released.Task; }
    }
}
