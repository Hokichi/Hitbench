using Soundie.Services.FileSystem;
namespace Soundie.Test.Presentation;
public sealed class StubOHsExportDirectoryLauncher : IOHsExportDirectoryLauncher
{
    public int Calls { get; private set; }
    public Func<string, CancellationToken, Task> Run { get; set; } = (_, _) => Task.CompletedTask;
    public Task OpenAsync(string directory, CancellationToken token) { Calls++; return Run(directory, token); }
}
