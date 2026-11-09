using Hitbench.Resources.DataModels;
using Hitbench.Services.Dialogs;

namespace Hitbench.Test.Presentation;

public sealed class StubOHsHitNormalDialogService : IOHsHitNormalDialogService
{
    public Func<OHsHitNormalConfiguration, CancellationToken, Task<OHsHitNormalConfiguration?>> Request { get; set; } =
        (_, _) => Task.FromResult<OHsHitNormalConfiguration?>(null);
    public Task<OHsHitNormalConfiguration?> RequestAsync(string projectPath, string mapsetDirectory, OHsHitNormalConfiguration current, CancellationToken token) => Request(current, token);
}
