using Hitbench.Resources.DataModels;
namespace Hitbench.Services;
public interface IOHsExportService
{
    Task<string> ExportAsync(OHsExportRequest request, CancellationToken token, IProgress<OHsExportProgress>? progress = null);
}
