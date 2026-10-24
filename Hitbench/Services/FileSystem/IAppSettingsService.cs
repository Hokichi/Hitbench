using Hitbench.Core.Entities;

namespace Hitbench.Services.FileSystem;

public interface IAppSettingsService
{
    Task SaveThemeAsync(int theme, CancellationToken cancellationToken);
    Task SaveVolumeAsync(int volume, CancellationToken cancellationToken);
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveOsuPathAsync(string osuPath, CancellationToken cancellationToken);
    Task SaveWindowPlacementAsync(bool isMaximized, double restoredWidth, double restoredHeight,
        CancellationToken cancellationToken);
}
