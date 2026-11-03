using Hitbench.Resources.DataModels;
using Hitbench.Services.Ui;

namespace Hitbench.Test.Presentation;

public sealed class StubOHsUpdateRestartService : IOHsUpdateRestartService
{
    public Func<OHsStagedUpdate, CancellationToken, Task<bool>> Restart { get; set; } = (_, _) => Task.FromResult(false);
    public Task<bool> TryRestartAsync(OHsStagedUpdate update, CancellationToken token) => Restart(update, token);
}
