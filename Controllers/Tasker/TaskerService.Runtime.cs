using System.Text.Json;
namespace z3nDash;

public sealed partial class TaskerService
{
    public object RuntimePage(string id,string kind,int offset,int limit)
    {
        ValidateId(id);offset=Math.Clamp(offset,0,10000000);limit=Math.Clamp(limit,1,500);
        List<Dictionary<string,string>> rows;long? totalPending=null;
        if(kind=="instances")rows=_scheduler.GetInstances(id).OrderBy(r=>r["runId"],StringComparer.Ordinal).Skip(offset).Take(limit+1).ToList();
        else{
            using var connection=OpenTaskConnection();using var cmd=connection.CreateCommand();
            cmd.CommandText="SELECT uuid,schedule_id,queued_at,status,priority,run_id,origin FROM schedule_queue WHERE schedule_id=@id AND status='pending' ORDER BY priority,queued_at,uuid LIMIT @limit OFFSET @offset";
            foreach(var pair in new Dictionary<string,object>{{"@id",id},{"@limit",limit+1},{"@offset",offset}}){var p=cmd.CreateParameter();p.ParameterName=pair.Key;p.Value=pair.Value;cmd.Parameters.Add(p);}
            using(var reader=cmd.ExecuteReader()){rows=new();
            while(reader.Read()){var row=new Dictionary<string,string>();for(var i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=reader.IsDBNull(i)?"":reader.GetValue(i).ToString()!;rows.Add(row);}}
            cmd.CommandText="SELECT COUNT(*) FROM schedule_queue WHERE schedule_id=@id AND status='pending'";totalPending=Convert.ToInt64(cmd.ExecuteScalar());
        }
        var more=rows.Count>limit;if(more)rows.RemoveAt(limit);
        return new{items=rows,next_cursor=more?(offset+limit).ToString():null,total_pending=totalPending};
    }
}
