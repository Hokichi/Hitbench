namespace osuHitsounder.Resources.DataModels;

public sealed class FolderSnapshot
{
    private readonly FolderEntry[] entries;
    public int Count => entries.Length;

    public FolderSnapshot(IEnumerable<FolderEntry> entries, CancellationToken cancellationToken = default)
    {
        this.entries = entries.Select(entry =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return entry;
        }).ToArray();
        try
        {
            Array.Sort(this.entries, (left, right) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var order = right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
                if (order == 0) order = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
                return order != 0 ? order : StringComparer.Ordinal.Compare(left.FullName, right.FullName);
            });
        }
        catch (InvalidOperationException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public IReadOnlyList<FolderEntry> ReadPage(int offset, int count)
    {
        if (offset < 0 || offset > Count) throw new ArgumentOutOfRangeException(nameof(offset));
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var rows = new FolderEntry[Math.Min(count, Count - offset)];
        for (var i = 0; i < rows.Length; i++)
            rows[i] = entries[offset + i];
        return rows;
    }
}
