using Hitbench.Resources.DataModels;
namespace Hitbench.Services.FileSystem;
internal sealed class SampleMetadataRead
{
    public CancellationTokenSource Cancellation { get; } = new();
    public Task<SampleFileMetadata> Task { get; set; } = null!;
    public int Waiters { get; set; }
}
