using System.Text.Json;
namespace z3nDash;

public sealed partial class BridgeOutput
{
    public JsonElement Read(string task,string? run,string? cursor,int limit,bool before=false,int byteLimit=262144)
    {
        lock(_gate){
            while(_entries.First!=null&&_entries.First.Value.At<DateTimeOffset.UtcNow.AddMinutes(-15))Remove(_entries.First);
            long after=0;var gap=cursor==null&&_dropped.Contains(task);
            if(cursor!=null){var parts=cursor.Split(':');if(parts.Length!=2||parts[0]!=_epoch||!long.TryParse(parts[1],out after)){gap=true;after=0;}}
            var source=_tasks.GetValueOrDefault(task)??new LinkedList<Entry>();
            if(after>0&&source.First!=null&&after<source.First.Value.Seq-1)gap=true;
            IEnumerable<Entry> matching=source.Where(e=>run==null||run.Length==0||e.Run==run);
            matching=cursor==null||before?matching.Where(e=>!before||after==0||e.Seq<after).TakeLast(Math.Clamp(limit,1,200)):matching.Where(e=>e.Seq>after).Take(Math.Clamp(limit,1,200));
            var items=new List<JsonElement>();var bytes=1024;long next=after,first=0;
            foreach(var entry in matching){if(bytes+entry.Data.Length+1>Math.Min(byteLimit,maxBytes))break;bytes+=entry.Data.Length+1;
                using var doc=JsonDocument.Parse(entry.Data);items.Add(doc.RootElement.Clone());next=entry.Seq;if(first==0)first=entry.Seq;}
            if(next==0)next=_sequence;
            return JsonSerializer.SerializeToElement(new{items,gap,next_cursor=_epoch+":"+next,previous_cursor=first>0?_epoch+":"+first:null});
        }
    }
    public long BufferedBytes{get{lock(_gate)return _total;}}
}
