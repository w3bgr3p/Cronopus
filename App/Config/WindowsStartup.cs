using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Cronopus;

internal static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (enabled) throw new PlatformNotSupportedException("Start with Windows is available only on Windows.");
            return;
        }

        using var key = enabled
            ? Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            : Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;
        var command = enabled
            ? BuildCommand(Environment.ProcessPath ?? throw new InvalidOperationException("Application executable path is unavailable."),
                Assembly.GetEntryAssembly()?.Location ?? "")
            : "";
        Apply(enabled, key, command);
    }

    internal static string BuildCommand(string processPath, string assemblyPath)
    {
        var command = $"\"{processPath}\"";
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new InvalidOperationException("Application assembly path is unavailable.");
            command += $" \"{assemblyPath}\"";
        }
        return command;
    }

    [SupportedOSPlatform("windows")]
    internal static void Apply(bool enabled, RegistryKey key, string command)
    {
        if (enabled) key.SetValue("Cronopus", command, RegistryValueKind.String);
        else key.DeleteValue("Cronopus", throwOnMissingValue: false);
    }
}
