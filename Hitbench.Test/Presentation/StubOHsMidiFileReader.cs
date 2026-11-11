using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Presentation;

public sealed class StubOHsMidiFileReader : IOHsMidiFileReader
{
    public Func<string, CancellationToken, Task<OHsMidiImportSource>> Read { get; set; } = (_, _) => Task.FromResult(Services.OHsMidiImportMapperTests.Source());
    public int Calls { get; private set; }
    public Task<OHsMidiImportSource> ReadAsync(string path, CancellationToken token) { Calls++; return Read(path, token); }
}
