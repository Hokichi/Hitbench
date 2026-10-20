using System.Diagnostics;
using System.IO;
namespace osuHitsounder.Services.FileSystem;
public sealed class OHsExportDirectoryLauncher : IOHsExportDirectoryLauncher
{
    public Task OpenAsync(string directory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(directory);
        OHsExportPublisher.RejectLink(full);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        using var process = Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}
