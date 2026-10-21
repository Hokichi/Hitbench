namespace osuHitsounder.Resources.DataModels;

public sealed class FolderSnapshot
{
    private readonly FolderEntry[] entries;
    private FolderSnapshot(FolderEntry[] orderedEntries) => entries = orderedEntries;
    public int Count => entries.Length;
    public static FolderSnapshot ForProjects(IEnumerable<FolderEntry> entries, bool groupPinned, CancellationToken token = default) =>
        new(entries, token, (left, right) =>
        {
            var order = groupPinned ? right.IsPinned.CompareTo(left.IsPinned) : 0;
            return order != 0 ? order : right.LastChanged.CompareTo(left.LastChanged);
        });

    public FolderSnapshot FilterProjects(string query, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        query = query.Trim();
        return ForProjects(entries.Where(entry =>
        {
            token.ThrowIfCancellationRequested();
            return query.Length == 0 || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) || entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.MapsetName.Contains(query, StringComparison.OrdinalIgnoreCase) || entry.MapsetFolderName.Contains(query, StringComparison.OrdinalIgnoreCase);
        }), query.Length == 0, token);
    }
    public FolderSnapshot FilterSongs(string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        query = query.Trim();
        if (query.Length == 0) return this;
        var filtered = entries.Where(entry =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return entry.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new FolderSnapshot(filtered);
    }

    public FolderSnapshot(IEnumerable<FolderEntry> entries, CancellationToken cancellationToken = default)
        : this(entries, cancellationToken, (left, right) => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc)) { }

    private FolderSnapshot(IEnumerable<FolderEntry> entries, CancellationToken cancellationToken, Comparison<FolderEntry> comparison)
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
                var order = comparison(left, right);
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
