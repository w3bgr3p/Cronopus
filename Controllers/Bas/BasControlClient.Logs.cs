using System.Text;

namespace Cronopus;

public sealed partial class BasControlClient
{
    private void ReadLogs(string folder, Dictionary<string, long> offsets)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(folder, "*.txt", options).Take(500))
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var offset = offsets.GetValueOrDefault(file);
                if (stream.Length < offset) offset = 0;
                if (stream.Length == offset) continue;
                stream.Position = Math.Max(offset, stream.Length - 65536);
                var bytes = new byte[(int)(stream.Length - stream.Position)]; stream.ReadExactly(bytes);
                var text = Encoding.UTF8.GetString(bytes);
                // Retain a partial UTF-8 line for the next poll rather than corrupting its characters.
                var end = text.LastIndexOf('\n');
                if (end < 0) continue;
                var complete = text[..(end + 1)];
                offsets[file] = stream.Position - bytes.Length + Encoding.UTF8.GetByteCount(complete);
                foreach (var line in complete.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    Log("log", Path.GetRelativePath(folder, file) + " · " + line.TrimEnd('\r'));
            }
            catch (IOException) { }
        }
    }
}
