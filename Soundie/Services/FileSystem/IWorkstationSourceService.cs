using Soundie.Core.Entities;

namespace Soundie.Services.FileSystem;

public interface IWorkstationSourceService
{
    Task<WorkstationSource> LoadAsync(string projectPath, CancellationToken token);
}
