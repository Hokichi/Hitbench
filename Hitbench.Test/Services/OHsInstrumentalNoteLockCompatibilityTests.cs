using Hitbench.Core.Entities;
using Hitbench.Services.ProjectFiles;

namespace Hitbench.Test.Services;

public class OHsInstrumentalNoteLockCompatibilityTests
{
    [Test]
    public void ExistingNotesDefaultToEditable()
    {
        var project = ProjectDataSerializer.Deserialize("{\"Tracks\":[{\"Kind\":\"Instrumental\",\"Notes\":[{\"Key\":\"C4\",\"Timestamp\":100}]}]}");
        Assert.That(((InstrumentalTrack)project.Tracks.Single()).Notes.Single().IsReadOnly, Is.False);
    }

    [Test]
    public void LockedNotesRoundTripAcrossZero()
    {
        var track = new InstrumentalTrack { Notes = [new() { Key = "C4", Timestamp = -100, IsReadOnly = true }, new() { Key = "E4", Timestamp = 10000, IsReadOnly = true }] };
        var reopened = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(new() { Tracks = [track] }));
        Assert.That(((InstrumentalTrack)reopened.Tracks.Single()).Notes.Select(n => (n.Timestamp, n.IsReadOnly)), Is.EqualTo(new[] { (-100, true), (10000, true) }));
    }

    [TestCase("\"IsReadOnly\":1", 100)]
    [TestCase("\"IsReadOnly\":true,\"isreadonly\":false", 100)]
    [TestCase("\"IsReadOnly\":false", -1)]
    public void InvalidLockDataIsRejected(string flag, int time)
    {
        var json = "{\"Tracks\":[{\"Kind\":\"Instrumental\",\"Notes\":[{\"Key\":\"C4\",\"Timestamp\":" + time + "," + flag + "}]}]}";
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize(json));
    }

    [Test]
    public void LockedResidentSurvivesReplacementOfEditableNeighbors()
    {
        var locked = new InstrumentalNote { Key = "C4", Timestamp = -100, IsReadOnly = true };
        var track = new InstrumentalTrack { Notes = [locked, new() { Key = "D4", Timestamp = 100 }] };
        Assert.That(track.TryRemoveNote(locked), Is.False);
        Assert.That(track.TryMoveNote(locked, "C4", 0), Is.False);
        Assert.That(track.TryReplaceNotes([new() { Key = "D4", Timestamp = 200 }]), Is.False);
        Assert.That(track.TryReplaceNotes([new() { Key = "C4", Timestamp = 0 }, new() { Key = "D4", Timestamp = 200 }]), Is.False);
        Assert.That(track.TryReplaceNotes([locked, new() { Key = "D4", Timestamp = 200 }]), Is.True);
        Assert.That(track.Notes.Single(n => n.IsReadOnly).Timestamp, Is.EqualTo(-100));
        Assert.That(track.TryAddNote(new() { Key = "F4", Timestamp = 200, IsReadOnly = true }), Is.False);
    }
}
