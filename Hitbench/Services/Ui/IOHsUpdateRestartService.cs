using Hitbench.Resources.DataModels;

namespace Hitbench.Services.Ui;

public interface IOHsUpdateRestartService
{
    Task<bool> TryRestartAsync(OHsStagedUpdate update, CancellationToken token);
}
