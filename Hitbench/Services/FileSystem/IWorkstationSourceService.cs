using Hitbench.Core.Entities;

namespace Hitbench.Services.FileSystem;

public interface IWorkstationSourceService
{
    Task<WorkstationSource> LoadAsync(string projectPath, CancellationToken token);
}
