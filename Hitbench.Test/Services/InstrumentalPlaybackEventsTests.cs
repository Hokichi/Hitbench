using Hitbench.Resources.DataModels;
using Hitbench.Services;
namespace Hitbench.Test.Services;

public class InstrumentalPlaybackEventsTests
{
    [Test]
    public void SharedLookupKeepsCanonicalCasingAndPlacementIdentity()
    {
        var notes = Enumerable.Range(0, 10000).Select(i => new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", i)).ToArray();
        var ids = notes.Select((n, i) => (n.Id, Value: (long)i + 1)).ToDictionary(p => p.Id, p => p.Value);
        var events = InstrumentalPlaybackEvents.BuildWithLookup(new Dictionary<string, string> { ["C4"] = "piano.wav" }, notes, 37,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["piano.wav"] = "PIANO.wav" }, id => ids[id]);
        Assert.That(events.Select(e => e.FileName), Is.All.EqualTo("PIANO.wav"));
        Assert.That(events.Select(e => e.TrackVolume), Is.All.EqualTo(37));
        Assert.That(events.Select(e => e.Id), Is.EqualTo(Enumerable.Range(1, 10000).Select(i => (long)i)));
        Assert.That(events.Select(e => e.Timestamp), Is.EqualTo(Enumerable.Range(0, 10000)));
    }
    [Test]
    public void ResolvesKeysIndividuallyAndDistinguishesUnknownAvailability()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var notes = new[] { new InstrumentalNoteSnapshot(a, "C4", 125), new InstrumentalNoteSnapshot(b, "E4", 125) };
        var keys = new Dictionary<string, string> { ["C4"] = "piano.wav", ["E4"] = "ep.wav" };
        var events = InstrumentalPlaybackEvents.Build(keys, notes, 75, ["PIANO.wav"], id => id == a ? 1 : 2);
        Assert.That(events, Has.Count.EqualTo(1)); Assert.That(events[0].TrackVolume, Is.EqualTo(75));
        Assert.That(InstrumentalPlaybackEvents.Build(keys, notes, 75, null, id => id == a ? 1 : 2), Has.Count.EqualTo(2));
        Assert.That(InstrumentalPlaybackEvents.Build(keys, notes, 75, [], id => 1), Is.Empty);
    }
}
