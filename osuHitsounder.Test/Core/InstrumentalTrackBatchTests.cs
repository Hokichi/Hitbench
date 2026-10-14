using osuHitsounder.Core.Entities;
namespace osuHitsounder.Test.Core;

public class InstrumentalTrackBatchTests
{
    [Test]
    public void InvalidReplacementKeepsOriginalArrangement()
    {
        var original = new InstrumentalNote { Key = "C4", Timestamp = 125, Volume = 70 };
        var track = new InstrumentalTrack { Notes = [original] };
        Assert.That(track.TryReplaceNotes([new() { Key = "E4", Timestamp = 250 }, new() { Key = "E4", Timestamp = 250 }]), Is.False);
        Assert.That(track.Notes.Single(), Is.SameAs(original));
        Assert.That(track.TryReplaceNotes([new() { Key = "B9", Timestamp = 500, Volume = 80 }]), Is.True);
        Assert.That(track.Notes.Single().Key, Is.EqualTo("B9"));
    }
}
