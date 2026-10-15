using System.Text;
using System.Text.Json;
namespace z3nDash;

public sealed partial class BridgeOutput(int maxBytes=262144,int maxLines=2000)
{
    private sealed record Entry(long Seq,string Task,string Run,byte[] Data,DateTimeOffset At)
    { public LinkedListNode<Entry>? AllNode; public LinkedListNode<Entry>? TaskNode; }
    private readonly LinkedList<Entry> _entries=new();
    private readonly Dictionary<string,LinkedList<Entry>> _tasks=new();
    private readonly Dictionary<string,long> _taskBytes=new();
    private readonly HashSet<string> _dropped=new();
    private readonly object _gate=new();
    private readonly Dictionary<string,Decoder> _decoders=new();
    private readonly string _epoch=Guid.NewGuid().ToString("N");
    private long _sequence;
    private long _total;
    public void Append(string task,string run,ReadOnlyMemory<byte> bytes)
    {
        lock(_gate){
            var key=task+":"+run;
            if(!_decoders.TryGetValue(key,out var decoder)){
                if(_decoders.Count>=5000)_decoders.Clear();_decoders[key]=decoder=Encoding.UTF8.GetDecoder();
            }
            if(bytes.Length>maxBytes){bytes=bytes[^maxBytes..];decoder.Reset();while(bytes.Length>0&&(bytes.Span[0]&0xC0)==0x80)bytes=bytes[1..];}
            var chars=new char[2048];
            do{decoder.Convert(bytes.Span,chars,false,out var used,out var written,out _);bytes=bytes[used..];
                if(written==0)continue;var now=DateTimeOffset.UtcNow;
                var payload=JsonSerializer.SerializeToUtf8Bytes(new{line=new string(chars,0,written),run,ts=now.ToString("o"),level="INFO"});
                var entry=new Entry(++_sequence,task,run,payload,now);
                entry.AllNode=_entries.AddLast(entry);
                if(!_tasks.TryGetValue(task,out var list))_tasks[task]=list=new();
                entry.TaskNode=list.AddLast(entry);_total+=payload.Length+192;
                _taskBytes[task]=_taskBytes.GetValueOrDefault(task)+payload.Length+192;
            }while(bytes.Length>0);
            while(_tasks.TryGetValue(task,out var taskEntries)&&(_taskBytes[task]>maxBytes||taskEntries.Count>maxLines))Remove(taskEntries.First!.Value.AllNode!);
            while(_entries.First!=null&&(_total>64L*1024*1024||_entries.First.Value.At<DateTimeOffset.UtcNow.AddMinutes(-15)))Remove(_entries.First);
        }
    }
    private void Remove(LinkedListNode<Entry> entry){if(_dropped.Count>=10000)_dropped.Clear();_dropped.Add(entry.Value.Task);var size=entry.Value.Data.Length+192;_total-=size;_taskBytes[entry.Value.Task]-=size;_entries.Remove(entry);var list=_tasks[entry.Value.Task];list.Remove(entry.Value.TaskNode!);if(list.Count==0){_tasks.Remove(entry.Value.Task);_taskBytes.Remove(entry.Value.Task);}}
}
