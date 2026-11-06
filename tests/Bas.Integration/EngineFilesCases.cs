using Cronopus;

static class EngineFilesCases
{
    public static void Run(string root)
    {
        var source = Path.Combine(root, "engine-source");
        var target = Path.Combine(root, "engine-copy");
        foreach (var directory in new[] { "c", "s", "ex/d/extension/_metadata" })
            Directory.CreateDirectory(Path.Combine(source, directory));
        File.WriteAllText(Path.Combine(source, "FastExecuteScript.exe"), "engine");
        File.WriteAllText(Path.Combine(source, "settings.ini"), "default");
        BasEngineFiles.Prepare(source, target, CancellationToken.None);
        foreach (var directory in new[] { "c", "s", "ex/d/extension/_metadata" })
            if (!Directory.Exists(Path.Combine(target, directory))) throw new Exception("Engine clone lost empty BAS directory: " + directory);
        // Existing v1 runtimes must be repaired without replacing their local settings.
        Directory.Delete(Path.Combine(target, "c"));
        Directory.Delete(Path.Combine(target, "s"));
        File.WriteAllText(Path.Combine(target, ".cronopus-ready"), "1");
        File.WriteAllText(Path.Combine(target, "settings.ini"), "private runtime");
        BasEngineFiles.Prepare(source, target, CancellationToken.None);
        if (!Directory.Exists(Path.Combine(target, "c")) || !Directory.Exists(Path.Combine(target, "s")))
            throw new Exception("Existing runtime did not regain BAS service directories");
        if (File.ReadAllText(Path.Combine(target, "settings.ini")) != "private runtime")
            throw new Exception("Directory migration replaced runtime settings");
        if (File.ReadAllText(Path.Combine(source, "settings.ini")) != "default") throw new Exception("Engine source was modified");
        Console.WriteLine("PASS empty BAS engine directories survive cloning and existing runtime migration");
    }
}
