using System.Text.Json;
using Cronopus;

static class UnloadCases
{
    public static async Task Run(string root)
    {
        var engine = new DelayedEngine();
        await using var service = new BasControlService(new BridgeStore(Path.Combine(root, "unload")), () => engine);
        var project = service.Save(new BasProjectEdit { Name = "Slow start", ProjectPath = TestProject.Create(root) });
        service.Run(project.Id, 1);
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var unloading = service.UnloadAsync(project.Id);
        engine.Ready.TrySetResult();
        await engine.Disposing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            if (service.List().Single().State != "unloading") throw new Exception("startup overwrote unloading state");
            try { service.Run(project.Id, 1); throw new Exception("run admitted during unload"); } catch (BasConflictException) { }
            var repeated = service.UnloadAsync(project.Id);
            engine.Released.TrySetResult();
            await Task.WhenAll(unloading, repeated);
            if (engine.Disposals != 1) throw new Exception("engine disposed more than once");
            Console.WriteLine("PASS startup/unload race, blocked new runs and shared unload completion");
        }
        finally { engine.Released.TrySetResult(); await unloading; }
    }
    private sealed class DelayedEngine : IBasControlEngine
    {
        public event Action<string, JsonElement>? Message;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Disposing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Disposals;
        public async Task StartAsync(BasProject project, CancellationToken ct) { Started.TrySetResult(); await Ready.Task; }
        public Task<JsonElement> RunAsync(JsonElement parameters, int threadId, CancellationToken ct)
            => Task.FromResult(JsonSerializer.SerializeToElement(5));
        public async ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); Disposing.TrySetResult(); await Released.Task; }
    }
}
