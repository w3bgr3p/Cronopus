using Cronopus;
using Microsoft.Win32;

internal static class StartupCases
{
    public static void Run()
    {
        var command = WindowsStartup.BuildCommand(@"C:\Program Files\Cronopus\Cronopus.exe", "unused.dll");
        if (command != "\"C:\\Program Files\\Cronopus\\Cronopus.exe\"")
            throw new Exception("Startup executable path must be quoted without inherited arguments");
        var hosted = WindowsStartup.BuildCommand(@"C:\Program Files\dotnet\dotnet.exe", @"C:\App Files\Cronopus.dll");
        if (hosted != "\"C:\\Program Files\\dotnet\\dotnet.exe\" \"C:\\App Files\\Cronopus.dll\"")
            throw new Exception("Framework-dependent startup must include the application assembly");
        if (!OperatingSystem.IsWindows()) return;
        var path = @"Software\Cronopus.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("OtherApp", "keep");
            WindowsStartup.Apply(true, key, command);
            WindowsStartup.Apply(true, key, command);
            if ((string?)key.GetValue("Cronopus") != command || key.GetValueKind("Cronopus") != RegistryValueKind.String)
                throw new Exception("Startup registration missing or invalid");
            WindowsStartup.Apply(true, key, hosted);
            if ((string?)key.GetValue("Cronopus") != hosted)
                throw new Exception("Startup command was not refreshed after relocation");
            WindowsStartup.Apply(false, key, command);
            WindowsStartup.Apply(false, key, command);
            if (key.GetValue("Cronopus") != null || (string?)key.GetValue("OtherApp") != "keep")
                throw new Exception("Disabling startup must remove only Cronopus");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
        Console.WriteLine("PASS Windows startup quoting, hosted launch, enable, relocation and disable in isolated registry key");
    }
}
