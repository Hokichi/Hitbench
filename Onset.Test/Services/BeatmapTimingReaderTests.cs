using System.Globalization;
using Onset.Core.Entities;
using Onset.Services.FileSystem;

namespace Onset.Test.Services;

public class BeatmapTimingReaderTests
{
    private string root = null!;
    private string beatmapPath = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "Onset-timing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        beatmapPath = Path.Combine(root, "map.osu");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    [Test]
    public void ReadsUninheritedPointsInSourceOrderAndRoundsAwayFromZero()
    {
        File.WriteAllText(beatmapPath, """
            [TimingPoints]
            -1.5,500,4,1,0,100,1,0
            1000,-100,4,1,0,100,0,0
            1000.5,400,4,1,0,100,1,0
            1000.5,400,4,1,0,100,1,0
            [HitObjects]
            ignored
            """);
        var project = new ProjectData();

        BeatmapTimingReader.Populate(project, beatmapPath, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(project.Offsets, Is.EqualTo(new[] { -2, 1001, 1001 }));
            Assert.That(project.BPMs, Is.EqualTo(new[] { 120, 150, 150 }));
        });
    }

    [Test]
    public void UsesInvariantNumbersAndRoundsBpm()
    {
        File.WriteAllText(beatmapPath, "\uFEFF [TimingPoints]\n0,128,4,1,0,100,1,0\n1,1920,4,1,0,100,1,0\n2,1600,4,1,0,100,1,0\n// comment\n");
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var project = new ProjectData();
            BeatmapTimingReader.Populate(project, beatmapPath, CancellationToken.None);
            Assert.That(project.BPMs, Is.EqualTo(new[] { 469, 31, 38 }));
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    [Test]
    public void MissingSectionProducesEmptyLists()
    {
        File.WriteAllText(beatmapPath, "[General]\nAudioFilename: song.mp3\n");
        var project = new ProjectData { Offsets = [1], BPMs = [2] };
        BeatmapTimingReader.Populate(project, beatmapPath, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(project.Offsets, Is.Empty);
            Assert.That(project.BPMs, Is.Empty);
        });
    }

    [TestCase("0,0,4,1,0,100,1,0")]
    [TestCase("0,-10,4,1,0,100,1,0")]
    [TestCase("0,NaN,4,1,0,100,1,0")]
    [TestCase("0,Infinity,4,1,0,100,1,0")]
    [TestCase("2147483648,500,4,1,0,100,1,0")]
    [TestCase("0,0.000001,4,1,0,100,1,0")]
    [TestCase("0,500,4,1,0,100,no,0")]
    [TestCase("0,500,4,1,0,100,1")]
    public void InvalidUninheritedPointReportsLineAndPreservesExistingLists(string row)
    {
        File.WriteAllText(beatmapPath, $"[TimingPoints]\n{row}\n");
        var project = new ProjectData { Offsets = [7], BPMs = [8] };
        var error = Assert.Throws<InvalidDataException>(() =>
            BeatmapTimingReader.Populate(project, beatmapPath, CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("line 2"));
            Assert.That(project.Offsets, Is.EqualTo(new[] { 7 }));
            Assert.That(project.BPMs, Is.EqualTo(new[] { 8 }));
        });
    }

    [Test]
    public void InheritedPointDoesNotRequireOtherNumericFields()
    {
        File.WriteAllText(beatmapPath, "[TimingPoints]\nanything,anything,anything,anything,anything,anything,0,anything\n");
        var project = new ProjectData();
        Assert.DoesNotThrow(() => BeatmapTimingReader.Populate(project, beatmapPath, CancellationToken.None));
        Assert.That(project.Offsets, Is.Empty);
    }

    [Test]
    public void CancellationPreservesExistingLists()
    {
        File.WriteAllText(beatmapPath, "[TimingPoints]\n0,500,4,1,0,100,1,0\n");
        var project = new ProjectData { Offsets = [7], BPMs = [8] };
        Assert.Throws<OperationCanceledException>(() =>
            BeatmapTimingReader.Populate(project, beatmapPath, new CancellationToken(true)));
        Assert.That(project.Offsets, Is.EqualTo(new[] { 7 }));
    }

    [Test]
    public void FindBeatmapUsesOrdinalTopLevelOrder()
    {
        File.WriteAllText(Path.Combine(root, "z.osu"), "");
        File.WriteAllText(Path.Combine(root, "a.osu"), "");
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        File.WriteAllText(Path.Combine(root, "nested", "0.osu"), "");
        Assert.That(BeatmapTimingReader.FindBeatmap(root), Is.EqualTo(Path.Combine(root, "a.osu")));
    }
}
