using ZennoLab.InterfacesLibrary.ProjectModel;

namespace Cronopus;

public sealed class CsxGlobals
{
    public TaskRunContext current_task => TaskRunContext.Current;
    public StubProject                    project  { get; init; } = null!;
    public Cronopus.Browser.PlaywrightInstance instance { get; init; } = null!;
    public Logger                         log      { get; init; } = null!;
}
