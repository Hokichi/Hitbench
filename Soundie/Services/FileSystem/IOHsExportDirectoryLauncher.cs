namespace Soundie.Services.FileSystem;
public interface IOHsExportDirectoryLauncher
{
    Task OpenAsync(string directory, CancellationToken token);
}
