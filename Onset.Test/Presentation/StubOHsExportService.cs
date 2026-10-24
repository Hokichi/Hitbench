using Onset.Resources.DataModels;
using Onset.Services;
namespace Onset.Test.Presentation;
public sealed class StubOHsExportService : IOHsExportService
{
    public int Calls { get; private set; }
    public OHsExportRequest? LastRequest { get; private set; }
    public Func<OHsExportRequest, CancellationToken, Task<string>> Run { get; set; } = (_, _) => Task.FromResult("Export");
    public IProgress<OHsExportProgress>? LastProgress { get; private set; }
    public Task<string> ExportAsync(OHsExportRequest request, CancellationToken token, IProgress<OHsExportProgress>? progress = null) { Calls++; LastRequest = request; LastProgress = progress; return Run(request, token); }
}
