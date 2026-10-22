using Soundie.Services.FileSystem;
namespace Soundie.Test.Presentation;
public sealed class StubDirectoryLauncher : IOHsExportDirectoryLauncher
{
    public List<string> Directories { get; } = [];
    public Func<CancellationToken, Task>? Open { get; set; }
    public Task OpenAsync(string directory, CancellationToken token) { token.ThrowIfCancellationRequested(); Directories.Add(directory); return Open?.Invoke(token) ?? Task.CompletedTask; }
}
