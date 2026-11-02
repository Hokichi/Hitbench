using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;

namespace Hitbench.Test.Services;

public class OHsExportRampTests
{
    [TestCase(".wav", false, false), TestCase(".ogg", false, false)]
    [TestCase(".wav", true, false), TestCase(".ogg", true, false)]
    [TestCase(".wav", false, true), TestCase(".ogg", false, true)]
    [TestCase(".wav", true, true), TestCase(".ogg", true, true)]
    public async Task PublishedTailUsesNativeLookupGain(string format, bool instrumental, bool initiallySilent)
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "Song [Original].osu");
        await File.WriteAllTextAsync(source, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 176400).ToArray());
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "a.wav"), audio, default);
        var time = initiallySilent ? 0 : 500;
        Track track = instrumental
            ? new InstrumentalTrack { KeyFiles = new() { ["C4"] = "a.wav" }, Notes = [new() { Key = "C4", Timestamp = time }] }
            : new Track { Samples = [new() { FileName = "a.wav", Timestamp = time }] };
        track.SampleType = SampleType.Normal; track.Addition = "Normal";
        var project = new ProjectData { Tracks = [track], ControlPoints = initiallySilent
            ? [new() { Volume = 0 }, new() { Timestamp = 100, Volume = 0, IsRamp = true }, new() { Timestamp = 1000, Volume = 100 }]
            : [new() { Volume = 10, IsRamp = true }, new() { Timestamp = 1000, Volume = 50 }, new() { Timestamp = 1500, Volume = 20 }] };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, "unused.wav", ProjectDataSerializer.Serialize(project), new("Hitsounds", 1, format, format, false));
        var directory = await new OHsExportService().ExportAsync(request, default);
        var text = await File.ReadAllTextAsync(Path.Combine(directory, "Song [Hitsounds].osu"));
        var pcm = OHsExportPlaybackOracle.Render(text, name => AudioDecoder.Decode(Path.Combine(directory, Path.ChangeExtension(name, format)), default), 110250);
        Assert.That(OHsExportPlaybackOracle.Resolve(text).Single(h => h.Type == (instrumental ? "whistle" : "normal")).Volume,
            Is.EqualTo(initiallySilent ? 0 : 30));
        foreach (var relativeFrame in new[] { 2205, 22050, 44100, 65000, 70000, 85000 })
        {
            var frame = time * 441 / 10 + relativeFrame;
            Assert.That(pcm[frame * 2], Is.EqualTo(initiallySilent ? 0 : .06).Within(format == ".wav" ? 2d / 32768 : .005));
            Assert.That(pcm[frame * 2 + 1], Is.EqualTo(pcm[frame * 2]).Within(.001));
        }
        Assert.That(Directory.GetFiles(directory, "*hit*" + format), Has.Length.EqualTo(instrumental ? 2 : 1));
        var samplePath = Directory.GetFiles(directory, (instrumental ? "*hitwhistle*" : "*hitnormal*") + format).Single();
        using var raw = AudioDecoder.Decode(samplePath, default);
        Assert.That(raw.FrameCount, Is.EqualTo(88200));
        Assert.That(raw.Samples[44100], Is.EqualTo(.2).Within(format == ".wav" ? 2d / 32768 : .005));
        if (format == ".wav")
            Assert.That(await File.ReadAllBytesAsync(samplePath), Is.EqualTo(await File.ReadAllBytesAsync(Path.Combine(root, "Samples", "a.wav"))));
    }

    [Test]
    public async Task MultipleRampHitsPublishLosslessExportWithinBudget()
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "Song [Original].osu");
        await File.WriteAllTextAsync(source, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.1f, 17640).ToArray());
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "a.wav"), audio, default);
        var project = new ProjectData { ControlPoints = [new() { Volume = 10, IsRamp = true }, new() { Timestamp = 2000, Volume = 80 }],
            Tracks = [new() { SampleType = SampleType.Normal, Samples = Enumerable.Range(0, 12).Select(i => new Hitbench.Core.Entities.Sample { FileName = "a.wav", Timestamp = 100 + i * 100 }).ToList() }] };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, "unused.wav", ProjectDataSerializer.Serialize(project), new("Hitsounds", 1, ".wav", ".wav", false));
        Directory.CreateDirectory(Path.Combine(root, "Export"));
        await File.WriteAllTextAsync(Path.Combine(root, "Export", "keep.txt"), "keep");
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var output = await new OHsExportService().ExportAsync(request, cancel.Token);
        var text = await File.ReadAllTextAsync(Path.Combine(output, "Song [Hitsounds].osu"));
        var pcm = OHsExportPlaybackOracle.Render(text, name => AudioDecoder.Decode(Path.Combine(output, name), cancel.Token), 66150);
        int[] volumes = [14, 17, 21, 24, 28, 31, 35, 38, 42, 45, 49, 52];
        Assert.That(OHsExportPlaybackOracle.Resolve(text).Select(h => h.Volume), Is.EqualTo(volumes));
        foreach (var ms in new[] { 150, 250, 650, 1150, 1250, 1350 })
        {
            var expected = Enumerable.Range(0, 12).Where(i => ms >= 100 + i * 100 && ms < 300 + i * 100)
                .Sum(i => .1 * volumes[i] / 100);
            var frame = ms * 44100 / 1000;
            Assert.That(pcm[frame * 2], Is.EqualTo(expected).Within(.0001));
            Assert.That(pcm[frame * 2 + 1], Is.EqualTo(expected).Within(.0001));
        }
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "keep.txt")), Is.EqualTo("keep"));
        Assert.That(Directory.GetFiles(output, "*hit*.wav"), Has.Length.EqualTo(1));
    }

    [Test]
    public void EqualAndDifferentRampSlopesReuseOnePhysicalFile()
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 44100).ToArray());
        var input = OHsExportRampPlanningTests.Input([new(100, 10, IsRamp: true), new(1100, 50), new(2100, 10, IsRamp: true), new(3100, 50)], [100, 2100]);
        Assert.That(OHsExportPlanner.Plan(input, default).AudioFileCount, Is.EqualTo(1));
        var altered = new OHsExportInput(input.Options, input.Beatmap, input.Voices, input.RequiredPoints,
            [new(100, 10, IsRamp: true), new(1100, 50), new(2100, 10, IsRamp: true), new(3100, 90)]);
        Assert.That(OHsExportPlanner.Plan(altered, default).AudioFileCount, Is.EqualTo(1));
    }

    [Test]
    public void OppositeSignTailsRetainPerFileClipping()
    {
        using var positive = DecodedAudio.CopyFrom(Enumerable.Repeat(.8f, 400).ToArray());
        using var negative = DecodedAudio.CopyFrom(Enumerable.Repeat(-.8f, 200).ToArray());
        var input = OHsExportRampPlanningTests.Input([], []);
        input = new(input.Options, input.Beatmap,
            [new(0, "positive.wav", 200, "Normal", SampleType.Normal, 0), new(2, "negative.wav", 100, "Normal", SampleType.Normal, 1)], input.RequiredPoints);
        var plan = OHsExportPlanner.Plan(input, default);
        var pcm = OHsExportPlaybackOracle.Render(OHsExportBeatmapWriter.Write(input, plan), name =>
            OHsExportAudio.Render(plan.Banks.SelectMany(b => b.Value.Select(s => (Name: OHsExportRepresentations.FileName(s.Key, b.Key, ".wav"), Asset: s.Value))).Single(p => p.Name == name).Asset,
                file => file == "positive.wav" ? positive : negative, default), 250);
        Assert.That(pcm[90 * 2], Is.EqualTo(.2f).Within(1e-6));
        Assert.That(pcm[199 * 2], Is.EqualTo(1));
    }

    [Test]
    public async Task ClippingPublishesAndAllowsRetry()
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var map = Path.Combine(root, "Song [Original].osu");
        await File.WriteAllTextAsync(map, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        using var positive = DecodedAudio.CopyFrom(Enumerable.Repeat(.8f, 400).ToArray());
        using var negative = DecodedAudio.CopyFrom(Enumerable.Repeat(-.8f, 200).ToArray());
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "positive.wav"), positive, default);
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "negative.wav"), negative, default);
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, map, "unused.wav", ProjectDataSerializer.Serialize(new ProjectData()), new("Hitsounds", 1, ".wav", ".wav", false));
        var exporter = new OHsExportService();
        var output = await exporter.ExportAsync(request, default);
        var project = new ProjectData { Tracks =
            [new() { Volume = 200, SampleType = SampleType.Normal, Samples = [new() { FileName = "positive.wav", Timestamp = 0 }] },
             new() { SampleType = SampleType.Normal, Samples = [new() { FileName = "negative.wav", Timestamp = 2 }] }] };
        var clippingRequest = request with { ProjectJson = ProjectDataSerializer.Serialize(project) };
        await exporter.ExportAsync(clippingRequest, default);
        var published = await File.ReadAllTextAsync(Path.Combine(output, "Song [Hitsounds].osu"));
        var pcm = OHsExportPlaybackOracle.Render(published, name => AudioDecoder.Decode(Path.Combine(output, name), default), 250);
        Assert.That(pcm[90 * 2], Is.EqualTo(.2f).Within(4d / 32768));
        await exporter.ExportAsync(clippingRequest, default);
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "Song [Hitsounds].osu")), Is.EqualTo(published));
        Assert.That(Directory.GetDirectories(root, ".Export-staging-*"), Is.Empty);
    }
}
