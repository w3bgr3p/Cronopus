using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System.Text.Json.Nodes;

namespace Cronopus;

internal static class LegacyDataImport
{
    public static void Run()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var target = Path.GetFullPath(AppContext.BaseDirectory);
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            foreach (var key in new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall\z3nDash_is1",
                         @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\z3nDash_is1" })
            {
                using var registration = hive.OpenSubKey(key);
                if (registration?.GetValue("InstallLocation") is string path && !string.IsNullOrWhiteSpace(path))
                    candidates.Add(path);
            }
        }
        candidates.Add(Path.Combine(local, "z3nDash"));
        candidates.Add(Path.Combine(Directory.GetParent(target.TrimEnd(Path.DirectorySeparatorChar))!.FullName, "z3nDash"));
        var legacy = candidates.FirstOrDefault(p => Directory.Exists(p)
            && !Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar).Equals(target.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            && (File.Exists(Path.Combine(p, "appsettings.secrets.json")) || File.Exists(Path.Combine(p, "z3nDash.exe"))));
        if (legacy != null) Import(target, legacy, local);
        else ImportBridge(local);
    }

    public static void Import(string target, string legacy, string localData)
    {
        target = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        legacy = Path.GetFullPath(legacy).TrimEnd(Path.DirectorySeparatorChar);
        if (target.Equals(legacy, StringComparison.OrdinalIgnoreCase)) return;
        if (target.StartsWith(legacy + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Install Cronopus outside the z3nDash directory to import its data.");
        ImportBridge(localData);
        var configPath = Path.Combine(target, "appsettings.secrets.json");
        var marker = Path.Combine(target, ".z3ndash-imported");
        if (File.Exists(configPath) || File.Exists(marker) || !Directory.Exists(legacy)) return;
        var sourceConfig = Path.Combine(legacy, "appsettings.secrets.json");
        var config = File.Exists(sourceConfig) ? JsonNode.Parse(File.ReadAllText(sourceConfig),
            new System.Text.Json.Nodes.JsonNodeOptions { PropertyNameCaseInsensitive = true }) : null;
        var db = config?["DbConfig"];
        var defaultDb = string.Equals((string?)db?["Type"], "sqlite", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace((string?)db?["SqlitePath"]);
        CopyMissing(legacy, target, root: true, defaultDb);
        if (config != null)
        {
            // Only relocate filesystem settings. Tokens and connection strings stay byte-for-byte values.
            foreach (var section in new[] { "DbConfig", "LogsConfig", "SecurityConfig" })
            {
                if (config[section] is not JsonObject settings) continue;
                foreach (var entry in settings.ToArray())
                    if ((entry.Key.EndsWith("Path", StringComparison.OrdinalIgnoreCase)
                        || entry.Key.EndsWith("Folder", StringComparison.OrdinalIgnoreCase))
                        && entry.Value is JsonValue value && value.TryGetValue<string>(out var path)
                        && path.StartsWith(legacy + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        settings[entry.Key] = target + path[legacy.Length..];
            }
            if (config["Crx"] is JsonObject extensions)
                foreach (var extension in extensions)
                    if (extension.Value?["Path"] is JsonValue value && value.TryGetValue<string>(out var path)
                        && path.StartsWith(legacy + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        extension.Value["Path"] = target + path[legacy.Length..];
            if (defaultDb) db!["SqlitePath"] = Path.Combine(target, "Cronopus.db");
            // Configuration is the completion marker: a failed copy can be retried before startup.
            File.WriteAllText(configPath + ".importing", config.ToJsonString());
            File.Move(configPath + ".importing", configPath, overwrite: false);
        }
        File.WriteAllText(marker, legacy);
    }

    private static void ImportBridge(string localData)
    {
        var source = Path.Combine(localData, "z3nDash", "bridge");
        var target = Path.Combine(localData, "Cronopus", "bridge");
        if (Directory.Exists(source) && !File.Exists(Path.Combine(target, "state.bin")))
            CopyMissing(source, target);
    }

    private static void CopyMissing(string source, string target, bool root = false, bool defaultDb = false)
    {
        Directory.CreateDirectory(target);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            if (root && (new[] { ".runtime", ".playwright", "wwwroot", "sdk", "updates" }
                .Contains(entry.Name, StringComparer.OrdinalIgnoreCase)
                || entry.Name.EndsWith(".WebView2", StringComparison.OrdinalIgnoreCase))) continue;
            var destination = Path.Combine(target, root && defaultDb && entry.Name.Equals("z3nDash.db", StringComparison.OrdinalIgnoreCase)
                ? "Cronopus.db" : entry.Name);
            if (entry is DirectoryInfo)
            {
                CopyMissing(entry.FullName, destination);
                continue;
            }
            if (root && (entry.Name.Equals("appsettings.secrets.json", StringComparison.OrdinalIgnoreCase)
                || entry.Name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)
                || entry.Name.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
                || entry.Name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                || entry.Name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
                || new[] { ".exe", ".dll", ".pdb" }.Contains(entry.Extension, StringComparer.OrdinalIgnoreCase))) continue;
            // Online SQLite backup includes committed WAL data without copying transient journals.
            if (entry.Name.EndsWith("-wal") || entry.Name.EndsWith("-shm") || entry.Name.EndsWith("-journal")) continue;
            if (File.Exists(destination)) continue;
            var temp = destination + ".importing";
            if (new[] { ".db", ".sqlite", ".sqlite3" }.Contains(entry.Extension, StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(temp)) File.Delete(temp);
                using var input = new SqliteConnection(new SqliteConnectionStringBuilder
                    { DataSource = entry.FullName, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                using var output = new SqliteConnection(new SqliteConnectionStringBuilder
                    { DataSource = temp, Pooling = false }.ToString());
                input.Open();
                output.Open();
                input.BackupDatabase(output);
            }
            else File.Copy(entry.FullName, temp, overwrite: true);
            File.Move(temp, destination, overwrite: false);
        }
    }
}
