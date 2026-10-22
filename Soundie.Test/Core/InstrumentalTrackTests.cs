using Soundie.Core.Constants;
using Soundie.Core.Entities;

namespace Soundie.Test.Core;

public class InstrumentalTrackTests
{
    [Test]
    public void KeysSpan108ChromaticPitches()
    {
        Assert.That(InstrumentalKeys.All, Has.Count.EqualTo(108));
        Assert.That(InstrumentalKeys.All.Distinct().Count(), Is.EqualTo(108));
        Assert.That(InstrumentalKeys.GetName(0), Is.EqualTo("C1"));
        Assert.That(InstrumentalKeys.GetName(107), Is.EqualTo("B9"));
        Assert.That(InstrumentalKeys.TryGetIndex("C0", out _), Is.False);
    }
    [Test]
    public void DomainRejectsCollisionsWithoutMutation()
    {
        var track = new InstrumentalTrack();
        var a = new InstrumentalNote { Key = "C4", Timestamp = 125 };
        var b = new InstrumentalNote { Key = "E4", Timestamp = 125 };
        Assert.That(track.TryAddNote(a), Is.True);
        Assert.That(track.TryAddNote(b), Is.True);
        Assert.That(track.TryMoveNote(b, "C4", 125), Is.False);
        Assert.That(b.Key, Is.EqualTo("E4"));
        Assert.That(track.TryAddNote(new() { Key = "C4", Timestamp = 125 }), Is.False);
        Assert.That(track.TryMoveNote(a, "B9", 250), Is.True);
        Assert.That(track.TryRemoveNote(a), Is.True);
    }
}
