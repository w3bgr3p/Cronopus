using System.Net;

namespace Cronopus;

public interface IScriptHandler
{
    string PathPrefix { get; }
    void Init();
    Task<bool> HandleRequest(HttpListenerContext context);
}
