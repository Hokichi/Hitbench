using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Presentation;

public sealed class StubOHsUpdateService : IOHsUpdateService
{
    public Func<CancellationToken, Task<OHsUpdateRelease?>> Check { get; set; } = _ => Task.FromResult<OHsUpdateRelease?>(null);
    public Func<OHsUpdateRelease, IProgress<double>, CancellationToken, Task<OHsStagedUpdate>> Stage { get; set; } = (_, _, _) => throw new NotImplementedException();
    public Task<OHsUpdateRelease?> CheckAsync(CancellationToken token) => Check(token);
    public Task<OHsStagedUpdate> StageAsync(OHsUpdateRelease release, IProgress<double> progress, CancellationToken token) => Stage(release, progress, token);
    public void Discard(OHsStagedUpdate update) { }
}
