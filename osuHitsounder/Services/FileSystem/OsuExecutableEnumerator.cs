using System.IO;
using System.IO.Enumeration;

namespace osuHitsounder.Services.FileSystem;

public sealed class OsuExecutableEnumerator : FileSystemEnumerator<string>
{
    private readonly CancellationToken cancellationToken;
    private readonly Action? onSkippedLocation;

    public OsuExecutableEnumerator(string root, CancellationToken cancellationToken, Action onSkippedLocation)
        : base(root, new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,
            IgnoreInaccessible = false
        })
    {
        this.cancellationToken = cancellationToken;
        this.onSkippedLocation = onSkippedLocation;
    }

    protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return !entry.IsDirectory && entry.FileName.Equals("osu!.exe", StringComparison.OrdinalIgnoreCase);
    }

    protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
            return true;

        onSkippedLocation?.Invoke();
        return false;
    }

    protected override string TransformEntry(ref FileSystemEntry entry) => entry.ToFullPath();

    protected override bool ContinueOnError(int error)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The base constructor can call this before our callback is assigned.
        // Let the service handle an inaccessible root in that case.
        if (onSkippedLocation is null || error is not (2 or 3 or 5 or 21 or 32 or 53 or 67 or 1117))
            return false;

        onSkippedLocation();
        return true;
    }
}
