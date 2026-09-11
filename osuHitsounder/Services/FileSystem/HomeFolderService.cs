using System.IO;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Services.FileSystem;

public sealed class HomeFolderService(string applicationDirectory, Func<IEnumerable<string>>? driveRoots = null)
    : IHomeFolderService
{
    public Task<HomeFolderData> LoadAsync(CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectsDirectory = Directory.CreateDirectory(Path.Combine(applicationDirectory, "projects"));
            var projects = ListFolders(projectsDirectory.FullName, cancellationToken);
            string? executable = null;
            var skipped = 0;
            void MarkSkipped() => Interlocked.Exchange(ref skipped, 1);

            using var search = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                await Parallel.ForEachAsync(driveRoots?.Invoke() ?? LocalDrives(MarkSkipped),
                    new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = search.Token },
                    (root, token) =>
                    {
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            using var entries = new OsuExecutableEnumerator(root, token, MarkSkipped);
                            while (entries.MoveNext())
                            {
                                token.ThrowIfCancellationRequested();
                                if (!File.Exists(entries.Current))
                                    continue;
                                if (Interlocked.CompareExchange(ref executable, entries.Current, null) is null)
                                    search.Cancel();
                                break;
                            }
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        {
                            MarkSkipped();
                        }
                        return ValueTask.CompletedTask;
                    });
            }
            catch (OperationCanceledException) when (executable is not null && !cancellationToken.IsCancellationRequested)
            {
                // First result cancels the other worker; caller cancellation still propagates.
            }

            cancellationToken.ThrowIfCancellationRequested();
            var osuDirectory = executable is null ? null : Path.GetDirectoryName(executable);
            var songsPath = osuDirectory is null ? null : Path.Combine(osuDirectory, "Songs");
            IReadOnlyList<DirectoryInfo> songs = [];
            var songsExist = false;
            if (songsPath is not null)
            {
                try
                {
                    songs = ListFolders(songsPath, cancellationToken);
                    songsExist = true;
                }
                catch (DirectoryNotFoundException)
                {
                    // Do not create folders in the user's osu! installation.
                }
            }
            return new HomeFolderData(projects, songs, osuDirectory, songsExist, skipped != 0);
        }, cancellationToken);

    private static DirectoryInfo[] ListFolders(string path, CancellationToken token) =>
        new DirectoryInfo(path).EnumerateDirectories("*", new EnumerationOptions
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = false
        }).Select(folder =>
        {
            token.ThrowIfCancellationRequested();
            return folder;
        }).OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
          .ThenBy(folder => folder.FullName, StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> LocalDrives(Action onSkippedLocation)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            string? root = null;
            try
            {
                if (drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.IsReady)
                    root = drive.RootDirectory.FullName;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                onSkippedLocation();
            }
            if (root is not null)
                yield return root;
        }
    }
}
