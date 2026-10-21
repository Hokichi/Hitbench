using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Services;
public interface IOHsExportService
{
    Task<string> ExportAsync(OHsExportRequest request, CancellationToken token, IProgress<OHsExportProgress>? progress = null);
}
