using System.Net;
namespace Cronopus;
public sealed class SchedulerHandler : IScriptHandler
{
    public string PathPrefix => "/tasker";
    public TaskerService Service { get; }
    public SchedulerHandler(DbConnectionService db, SchedulerService scheduler, string root)
        : this(new TaskerService(db, scheduler, root)) { }
    public SchedulerHandler(TaskerService service) { Service = service; }
    public void Init() { }
    public Task<bool> HandleRequest(HttpListenerContext context) => Service.HandleRequest(new TaskerExchange(context));
    public static string ShowPicker(string kind, string ext, string start) => TaskerService.ShowPicker(kind, ext, start);
}
