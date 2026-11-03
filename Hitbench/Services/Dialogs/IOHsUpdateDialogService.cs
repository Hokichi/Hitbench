using Hitbench.Resources.DataModels;

namespace Hitbench.Services.Dialogs;

public interface IOHsUpdateDialogService
{
    Task<OHsStagedUpdate?> ShowAsync(OHsUpdateRelease release, CancellationToken token);
}
