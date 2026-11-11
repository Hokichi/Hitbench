using Hitbench.Resources.DataModels;

namespace Hitbench.Services.FileSystem;

public interface IOHsMidiFileReader
{
    Task<OHsMidiImportSource> ReadAsync(string path, CancellationToken token);
}
