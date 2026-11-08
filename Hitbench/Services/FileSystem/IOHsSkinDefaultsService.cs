using Hitbench.Resources.DataModels;

namespace Hitbench.Services.FileSystem;

public interface IOHsSkinDefaultsService
{
    Task<OHsSkinDefaultsSnapshot> RefreshAsync(string projectPath, string currentSkin, bool allowDefaults, bool force, CancellationToken token);
}
