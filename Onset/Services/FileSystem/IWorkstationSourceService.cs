using Onset.Core.Entities;

namespace Onset.Services.FileSystem;

public interface IWorkstationSourceService
{
    Task<WorkstationSource> LoadAsync(string projectPath, CancellationToken token);
}
