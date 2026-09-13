using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services.FileSystem;

public interface IAppSettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveOsuPathAsync(string osuPath, CancellationToken cancellationToken);
}

