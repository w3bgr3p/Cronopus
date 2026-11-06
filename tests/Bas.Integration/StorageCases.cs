using Cronopus;

static class StorageCases
{
    public static void Run(string root)
    {
        var source = Path.Combine(root, "engine-source"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "FastExecuteScript.exe"), "immutable binary");
        File.WriteAllText(Path.Combine(source, "settings.ini"), "original settings");
        var destination = Path.Combine(root, "owned-runtime");
        BasEngineFiles.Prepare(source, destination, CancellationToken.None);
        File.WriteAllText(Path.Combine(destination, "settings.ini"), "changed settings");
        if (File.ReadAllText(Path.Combine(source, "settings.ini")) != "original settings") throw new Exception("shared mutable settings were changed");
        var runs = Path.Combine(root, "retention"); Directory.CreateDirectory(runs);
        for (var n = 0; n < 35; n++) { var folder = Path.Combine(runs, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "log.txt"), "log"); }
        var unrelated = Path.Combine(runs, "keep-me"); Directory.CreateDirectory(unrelated);
        BasRunStorage.Prune(runs);
        if (Directory.GetDirectories(runs).Length != 30 || !Directory.Exists(unrelated)) throw new Exception("retention did not preserve its boundary");
        Console.WriteLine("PASS isolated mutable settings, shared engine layout and bounded native run retention");
    }
}
