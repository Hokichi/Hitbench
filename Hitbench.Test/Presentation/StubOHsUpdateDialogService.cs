using Hitbench.Resources.DataModels;
using Hitbench.Services.Dialogs;

namespace Hitbench.Test.Presentation;

public sealed class StubOHsUpdateDialogService : IOHsUpdateDialogService
{
    public Func<OHsUpdateRelease, CancellationToken, Task<OHsStagedUpdate?>> Present { get; set; } = (_, _) => Task.FromResult<OHsStagedUpdate?>(null);
    public Task<OHsStagedUpdate?> ShowAsync(OHsUpdateRelease release, CancellationToken token) => Present(release, token);
}
