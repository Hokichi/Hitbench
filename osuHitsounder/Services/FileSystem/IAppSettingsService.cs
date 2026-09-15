using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services.FileSystem;

public interface IAppSettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveOsuPathAsync(string osuPath, CancellationToken cancellationToken);
    Task SaveWindowPlacementAsync(bool isMaximized, double restoredWidth, double restoredHeight,
        CancellationToken cancellationToken);
}
