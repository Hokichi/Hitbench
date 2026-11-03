using Hitbench.Resources.DataModels;

namespace Hitbench.Services.FileSystem;

public interface IOHsUpdateService
{
    Task<OHsUpdateRelease?> CheckAsync(CancellationToken token);
    Task<OHsStagedUpdate> StageAsync(OHsUpdateRelease release, IProgress<double> progress, CancellationToken token);
    void Discard(OHsStagedUpdate update);
}
