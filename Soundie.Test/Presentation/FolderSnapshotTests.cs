using Soundie.Resources.DataModels;

namespace Soundie.Test.Presentation;

public class FolderSnapshotTests
{
    [TestCase("artist", "ARTIST middle", "", "", "")]
    [TestCase("title", "", "a TITLE end", "", "")]
    [TestCase("display", "", "", "a DISPLAY end", "")]
    [TestCase("folder", "", "", "", "a FOLDER end")]
    [TestCase("[.*]", "", "before [.*] after", "", "")]
    [TestCase("音楽", "音楽家", "", "", "")]
    public void FilterFindsLiteralSubstringInAnyField(string query, string artist, string title, string display, string name)
    {
        var entry = new FolderEntry("matched", name, DateTimeOffset.UnixEpoch)
        { Artist = artist, Title = title, DisplayName = display };
        var snapshot = new FolderSnapshot([entry, new FolderEntry("other", "other", DateTimeOffset.MinValue)]);
        Assert.That(snapshot.FilterSongs("  " + query + "  ").ReadPage(0, 10), Is.EqualTo(new[] { entry }));
    }

    [Test]
    public void FilterRestoresBlankQueryPreservesOrderAndSupportsCancellation()
    {
        var snapshot = new FolderSnapshot([
            new FolderEntry("old", "match old", DateTimeOffset.MinValue),
            new FolderEntry("new", "match new", DateTimeOffset.UtcNow)]);
        Assert.That(snapshot.FilterSongs("  "), Is.SameAs(snapshot));
        Assert.That(snapshot.FilterSongs("match").ReadPage(0, 10).Select(x => x.FullName), Is.EqualTo(new[] { "new", "old" }));
        Assert.That(snapshot.FilterSongs("absent").Count, Is.Zero);
        Assert.Throws<OperationCanceledException>(() => snapshot.FilterSongs("match", new CancellationToken(true)));
    }
}
