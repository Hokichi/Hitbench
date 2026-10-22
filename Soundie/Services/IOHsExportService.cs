using Soundie.Resources.DataModels;
namespace Soundie.Services;
public interface IOHsExportService
{
    Task<string> ExportAsync(OHsExportRequest request, CancellationToken token, IProgress<OHsExportProgress>? progress = null);
}
