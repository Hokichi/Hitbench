using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services.FileSystem;

public interface IWorkstationSourceService
{
    Task<WorkstationSource> LoadAsync(string projectPath, CancellationToken token);
}
