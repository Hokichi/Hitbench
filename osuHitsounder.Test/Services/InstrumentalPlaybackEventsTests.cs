using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
namespace osuHitsounder.Test.Services;

public class InstrumentalPlaybackEventsTests
{
    [Test]
    public void ResolvesKeysIndividuallyAndDistinguishesUnknownAvailability()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var notes = new[] { new InstrumentalNoteSnapshot(a, "C4", 125, 70), new InstrumentalNoteSnapshot(b, "E4", 125, 100) };
        var keys = new Dictionary<string, string> { ["C4"] = "piano.wav", ["E4"] = "ep.wav" };
        var events = InstrumentalPlaybackEvents.Build(keys, notes, 75, ["PIANO.wav"], id => id == a ? 1 : 2);
        Assert.That(events, Has.Count.EqualTo(1)); Assert.That(events[0].BeatVolume, Is.EqualTo(70)); Assert.That(events[0].TrackVolume, Is.EqualTo(75));
        Assert.That(InstrumentalPlaybackEvents.Build(keys, notes, 75, null, id => id == a ? 1 : 2), Has.Count.EqualTo(2));
        Assert.That(InstrumentalPlaybackEvents.Build(keys, notes, 75, [], id => 1), Is.Empty);
    }
}
