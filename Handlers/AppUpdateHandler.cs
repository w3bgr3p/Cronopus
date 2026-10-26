using System.Net;
using System.Text.Json;

namespace z3nDash;

internal sealed class AppUpdateHandler(AppUpdateService updates) : IScriptHandler
{
    public string PathPrefix => "/app-update";
    public void Init() { }

    public async Task<bool> HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var path = request.Url!.AbsolutePath;
        if (path == "/app-update/status" && request.HttpMethod == "GET")
        {
            response.Headers["Cache-Control"] = "no-store";
            await HttpHelpers.WriteJson(response, updates.Status());
            return true;
        }
        if (request.HttpMethod != "POST") { response.StatusCode = 405; return true; }
        // The dashboard is loopback-only, but browsers can still send cross-origin requests.
        var origin = request.Headers["Origin"];
        if (request.Headers["X-z3nDash-Update"] != "1" || !request.IsLocal
            || (origin != null && origin != request.Url.GetLeftPart(UriPartial.Authority)))
        {
            response.StatusCode = 403;
            await HttpHelpers.WriteJson(response, new { error = "Update actions require the local dashboard." });
            return true;
        }
        if (path is "/app-update/check" or "/app-update/install")
        {
            _ = updates.RunAsync(install: path.EndsWith("/install", StringComparison.Ordinal));
            response.StatusCode = 202;
            await HttpHelpers.WriteJson(response, updates.Status());
            return true;
        }
        if (path == "/app-update/settings")
        {
            try
            {
                using var json = await JsonDocument.ParseAsync(request.InputStream);
                await updates.SetAutomaticAsync(json.RootElement.GetProperty("automatic").GetBoolean());
                await HttpHelpers.WriteJson(response, updates.Status());
            }
            catch (Exception ex)
            {
                response.StatusCode = 400;
                await HttpHelpers.WriteJson(response, new { error = ex.Message });
            }
            return true;
        }
        response.StatusCode = 404;
        return true;
    }
}
