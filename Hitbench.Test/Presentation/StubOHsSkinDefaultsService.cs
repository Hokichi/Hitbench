using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Presentation;

public sealed class StubOHsSkinDefaultsService : IOHsSkinDefaultsService
{
    public Func<string, string, bool, bool, CancellationToken, Task<OHsSkinDefaultsSnapshot>> Refresh { get; set; } =
        (_, skin, _, _, _) => Task.FromResult(new OHsSkinDefaultsSnapshot(skin, [], false));
    public Task<OHsSkinDefaultsSnapshot> RefreshAsync(string path, string skin, bool allowed, bool force, CancellationToken token)
        => Refresh(path, skin, allowed, force, token);
}
