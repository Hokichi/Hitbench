using Hitbench.Resources.DataModels;

namespace Hitbench.Services.Dialogs;

public interface IOHsHitNormalDialogService
{
    Task<OHsHitNormalConfiguration?> RequestAsync(string projectPath, string mapsetDirectory, OHsHitNormalConfiguration current, CancellationToken token);
}
