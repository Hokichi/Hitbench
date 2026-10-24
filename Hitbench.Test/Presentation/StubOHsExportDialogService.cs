using Hitbench.Resources.DataModels;
using Hitbench.Services.Dialogs;
namespace Hitbench.Test.Presentation;
public sealed class StubOHsExportDialogService : IOHsExportDialogService
{
    public int Calls { get; private set; }
    public OHsExportRequest? LastRequest { get; private set; }
    public Func<OHsExportRequest, CancellationToken, Task> Run { get; set; } = (_, _) => Task.CompletedTask;
    public Task ShowAsync(OHsExportRequest request, CancellationToken token) { Calls++; LastRequest = request; return Run(request, token); }
}
