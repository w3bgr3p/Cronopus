namespace z3nDash;

internal sealed class TaskerBuffer : MemoryStream
{
    public bool Tail { get; set; }
    public Action<string>? Progress { get; set; }
    private long _lastProgress;
    private const int JsonLimit=8388608;
    private const int TailLimit=262144;
    public override void Write(ReadOnlySpan<byte> bytes)
    {
        var limit=Tail?TailLimit:JsonLimit;
        if(!Tail&&Length+bytes.Length>limit)throw new ArgumentException("Response exceeds 8 MiB");
        if(bytes.Length>limit)bytes=bytes[^limit..];
        var keep=(int)Math.Min(Length,limit-bytes.Length);
        if(keep<Length)GetBuffer().AsSpan((int)Length-keep,keep).CopyTo(GetBuffer());
        SetLength(keep+bytes.Length);bytes.CopyTo(GetBuffer().AsSpan(keep));Position=Length;
        if(Tail&&Progress!=null&&Environment.TickCount64-_lastProgress>1000){_lastProgress=Environment.TickCount64;Progress(System.Text.Encoding.UTF8.GetString(ToArray()));}
    }
    public override void Write(byte[] buffer,int offset,int count)=>Write(buffer.AsSpan(offset,count));
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default)
    {ct.ThrowIfCancellationRequested();Write(buffer.Span);return ValueTask.CompletedTask;}
    public override Task WriteAsync(byte[] buffer,int offset,int count,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();Write(buffer.AsSpan(offset,count));return Task.CompletedTask;}
}
