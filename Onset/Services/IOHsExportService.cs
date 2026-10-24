using Onset.Resources.DataModels;
namespace Onset.Services;
public interface IOHsExportService
{
    Task<string> ExportAsync(OHsExportRequest request, CancellationToken token, IProgress<OHsExportProgress>? progress = null);
}
