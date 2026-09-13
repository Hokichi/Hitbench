using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Test.Services;

public class FolderSnapshotTests
{
    [Test]
    public void PagesPreserveGlobalDescendingOrderAndBoundaries()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = Enumerable.Range(0, 130).Select(i =>
            new FolderEntry($@"C:\Songs\{i:D3}", $"{i:D3}", now.AddMinutes(i))
                { DisplayName = $"作曲者 - 曲{i}" });
        var snapshot = new FolderSnapshot(entries);
        Assert.That(snapshot.Count, Is.EqualTo(130));
        var first = snapshot.ReadPage(0, 64);
        Assert.That(first, Has.Count.EqualTo(64));
        Assert.That(first[0].Name, Is.EqualTo("129"));
        Assert.That(first[63].Name, Is.EqualTo("066"));
        var next = snapshot.ReadPage(64, 64);
        Assert.That(next[0].Name, Is.EqualTo("065"));
        Assert.That(next[0].DisplayName, Is.EqualTo("作曲者 - 曲65"));
        Assert.That(next[0].Directory.FullName, Is.EqualTo(@"C:\Songs\065"));
        Assert.That(next[63].Name, Is.EqualTo("002"));
        Assert.That(snapshot.ReadPage(128, 64).Select(x => x.Name), Is.EqualTo(new[] { "001", "000" }));
        Assert.That(snapshot.ReadPage(130, 64), Is.Empty);
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.ReadPage(-1, 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.ReadPage(131, 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.ReadPage(0, -1));
    }

    [Test]
    public void TiesAreDeterministicAndSnapshotDoesNotFollowSourceChanges()
    {
        var time = DateTimeOffset.UtcNow;
        var entries = new List<FolderEntry>
        {
            new(@"C:\z", "z", time),
            new(@"C:\b\A", "A", time),
            new(@"C:\a\a", "a", time)
        };
        var snapshot = new FolderSnapshot(entries);
        entries.Clear();
        Assert.That(snapshot.ReadPage(0, 64).Select(x => x.FullName),
            Is.EqualTo(new[] { @"C:\a\a", @"C:\b\A", @"C:\z" }));
    }
}
