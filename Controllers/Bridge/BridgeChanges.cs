using System.Text.Json;
namespace z3nDash;

public sealed class BridgeChanges(TaskerService tasker)
{
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly Dictionary<string,string> _known=new();
    private readonly Queue<(long Sequence,JsonElement Change)> _changes=new();
    private string _epoch="";
    private long _sequence;
    private DateTimeOffset _sampled;
    public string Cursor=>tasker.DatabaseEpoch+":"+_sequence;
    public async Task<object> Read(string? cursor,int limit,CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try{
            if(_epoch!=tasker.DatabaseEpoch){_known.Clear();_changes.Clear();_sequence=0;_epoch=tasker.DatabaseEpoch;_sampled=default;}
            if(DateTimeOffset.UtcNow-_sampled>TimeSpan.FromSeconds(1)){
                var current=new Dictionary<string,string>();string? next=null;
                do{
                    var page=JsonSerializer.SerializeToElement(tasker.Page(500,next));
                    foreach(var row in page.GetProperty("items").EnumerateArray()){
                        var id=row.GetProperty("id").GetString()!;var text=row.GetRawText();current[id]=text;
                        if(!_known.TryGetValue(id,out var old)||old!=text)Add(new{kind="upsert",task=row});
                    }
                    next=page.GetProperty("next_cursor").GetString();
                }while(next!=null);
                foreach(var removed in _known.Keys.Except(current.Keys))Add(new{kind="delete",id=removed});
                _known.Clear();foreach(var item in current)_known[item.Key]=item.Value;_sampled=DateTimeOffset.UtcNow;
            }
            long after=0;var parts=cursor?.Split(':');
            var reset=parts==null||parts.Length!=2||parts[0]!=_epoch||!long.TryParse(parts[1],out after)||(_changes.Count>0&&after<_changes.Peek().Sequence-1);
            if(reset)return new{items=Array.Empty<object>(),next_cursor=Cursor,reset_required=true,has_more=false};
            var rows=_changes.Where(c=>c.Sequence>after).Take(Math.Clamp(limit,1,500)).ToArray();
            var last=rows.Length>0?rows[^1].Sequence:_sequence;
            return new{items=rows.Select(r=>r.Change).ToArray(),next_cursor=_epoch+":"+last,reset_required=false,has_more=last<_sequence};
        }finally{_gate.Release();}
    }
    private void Add(object change){_changes.Enqueue((++_sequence,JsonSerializer.SerializeToElement(change)));while(_changes.Count>10000)_changes.Dequeue();}
}
