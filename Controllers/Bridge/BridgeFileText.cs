using System.Security.Cryptography;
using System.Text;

namespace z3nDash;

public sealed class BridgeFileText
{
    private const int MaxBytes = 1024 * 1024;
    private static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string FullPath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Absolute server file path required");
        return Path.GetFullPath(path);
    }
    private static async Task<byte[]> Read(FileStream stream, CancellationToken ct)
    {
        if (stream.Length > MaxBytes) throw new ArgumentException("Text editor supports files up to 1 MiB");
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, ct);
        return bytes;
    }
    private static (Encoding Encoding, int Bom, string Text) Decode(byte[] bytes)
    {
        Encoding encoding = new UTF8Encoding(false, true); var bom = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 })) { encoding = new UTF32Encoding(false, true, true); bom = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF })) { encoding = new UTF32Encoding(true, true, true); bom = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { encoding = new UTF8Encoding(true, true); bom = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding(false, true, true); bom = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding(true, true, true); bom = 2; }
        string text;
        try { text = encoding.GetString(bytes, bom, bytes.Length - bom); }
        catch (DecoderFallbackException) { throw new ArgumentException("Not supported text: use UTF-8 or Unicode with a BOM"); }
        if (text.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t' and not '\f'))
            throw new ArgumentException("Binary files cannot be edited as text");
        return (encoding, bom, text);
    }
    public async Task<object?> Save(string path, string revision, string text, CancellationToken ct)
    {
        path = FullPath(path);
        if (string.IsNullOrEmpty(revision)) throw new ArgumentException("File revision required. Open the file before saving.");
        // Keep other writers out between the revision check and the completed write.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var original = await Read(stream, ct);
        if (!string.Equals(Revision(original), revision, StringComparison.Ordinal)) return null;
        var decoded = Decode(original);
        byte[] content;
        try { content = decoded.Encoding.GetBytes(text); }
        catch (EncoderFallbackException) { throw new ArgumentException("The edited text contains invalid Unicode"); }
        var bytes = decoded.Encoding.GetPreamble().Concat(content).ToArray();
        if (bytes.Length > MaxBytes) throw new ArgumentException("Text editor supports files up to 1 MiB");
        ct.ThrowIfCancellationRequested();
        stream.Position = 0;
        // Finish a started save even if the client disconnects.
        await stream.WriteAsync(bytes, CancellationToken.None);
        stream.SetLength(bytes.Length); stream.Flush(true);
        return new { path, revision = Revision(bytes) };
    }
    public async Task<object> Open(string path, CancellationToken ct)
    {
        path = FullPath(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = await Read(stream, ct); var decoded = Decode(bytes);
        return new { path, text = decoded.Text, revision = Revision(bytes), encoding = decoded.Encoding.WebName, bom = decoded.Bom > 0 };
    }
}
