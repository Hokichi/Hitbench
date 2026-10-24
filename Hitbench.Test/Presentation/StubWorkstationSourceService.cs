using Hitbench.Core.Entities;
using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Presentation;

public sealed class StubWorkstationSourceService : IWorkstationSourceService
{
    public Func<string, CancellationToken, Task<WorkstationSource>> Load { get; set; } =
        (_, _) => Task.FromResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData { Offsets = [0], BPMs = [120] }));

    public Task<WorkstationSource> LoadAsync(string projectPath, CancellationToken token) => Load(projectPath, token);
}
