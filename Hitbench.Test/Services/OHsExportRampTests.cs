using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;

namespace Hitbench.Test.Services;

public class OHsExportRampTests
{
    [TestCase(".wav"), TestCase(".ogg")]
    public async Task PublishedTailFollowsRampAndLaterFixedStep(string format)
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "Song [Original].osu");
        await File.WriteAllTextAsync(source, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 176400).ToArray());
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "a.wav"), audio, default);
        var project = new ProjectData { ControlPoints = [new() { Volume = 10, IsRamp = true }, new() { Timestamp = 1000, Volume = 50 }, new() { Timestamp = 1500, Volume = 20 }],
            Tracks = [new() { SampleType = SampleType.Normal, Samples = [new() { FileName = "a.wav", Timestamp = 0 }] }] };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, "unused.wav", ProjectDataSerializer.Serialize(project), new("Hitsounds", 1, format, format, false));
        var directory = await new OHsExportService().ExportAsync(request, default);
        var text = await File.ReadAllTextAsync(Path.Combine(directory, "Song [Hitsounds].osu"));
        var pcm = OHsExportPlaybackOracle.Render(text, name => AudioDecoder.Decode(Path.Combine(directory, Path.ChangeExtension(name, format)), default), 88200);
        foreach (var frame in new[] { 2205, 22050, 44100, 65000, 70000, 85000 })
        {
            var gain = frame < 44100 ? .1 + .4 * frame / 44100 : frame < 66150 ? .5 : .2;
            Assert.That(pcm[frame * 2], Is.EqualTo(.2 * gain).Within(format == ".wav" ? 2d / 32768 : .005));
            Assert.That(pcm[frame * 2 + 1], Is.EqualTo(pcm[frame * 2]).Within(.001));
        }
        Assert.That(Directory.GetFiles(directory, "*hit*" + format), Has.Length.EqualTo(1));
    }

    [Test]
    public void EqualRelativeRampsReusePhysicalFileAndDifferentSlopesDoNot()
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 44100).ToArray());
        var input = OHsExportRampPlanningTests.Input([new(100, 10, IsRamp: true), new(1100, 50), new(2100, 10, IsRamp: true), new(3100, 50)], [100, 2100]);
        Assert.That(OHsExportPlanner.Plan(input, default, resolve: _ => audio).AudioFileCount, Is.EqualTo(1));
        var altered = new OHsExportInput(input.Options, input.Beatmap, input.Voices, input.RequiredPoints,
            [new(100, 10, IsRamp: true), new(1100, 50), new(2100, 10, IsRamp: true), new(3100, 90)]);
        Assert.That(OHsExportPlanner.Plan(altered, default, resolve: _ => audio).AudioFileCount, Is.EqualTo(2));
    }

    [Test]
    public void UnrepresentableCancellationRejectsBeforePublication()
    {
        using var positive = DecodedAudio.CopyFrom(Enumerable.Repeat(.8f, 400).ToArray());
        using var negative = DecodedAudio.CopyFrom(Enumerable.Repeat(-.8f, 200).ToArray());
        var input = OHsExportRampPlanningTests.Input([], []);
        input = new(input.Options, input.Beatmap,
            [new(0, "positive.wav", 200, "Normal", SampleType.Normal, 0), new(2, "negative.wav", 100, "Normal", SampleType.Normal, 1)], input.RequiredPoints);
        Assert.Throws<InvalidDataException>(() => OHsExportPlanner.Plan(input, default, resolve: name => name == "positive.wav" ? positive : negative));
    }

    [Test]
    public async Task RejectedClippingPreservesPublishedFilesAndManifest()
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
        var prior = Directory.GetFiles(output).ToDictionary(p => p, File.ReadAllBytes);
        var manifestPath = Path.Combine(root, ".osuhitsounder-export.json");
        var manifest = await File.ReadAllBytesAsync(manifestPath);
        var project = new ProjectData { Tracks =
            [new() { Volume = 200, SampleType = SampleType.Normal, Samples = [new() { FileName = "positive.wav", Timestamp = 0 }] },
             new() { SampleType = SampleType.Normal, Samples = [new() { FileName = "negative.wav", Timestamp = 2 }] }] };
        Assert.ThrowsAsync<InvalidDataException>(() => exporter.ExportAsync(request with { ProjectJson = ProjectDataSerializer.Serialize(project) }, default));
        foreach (var pair in prior) Assert.That(await File.ReadAllBytesAsync(pair.Key), Is.EqualTo(pair.Value));
        Assert.That(await File.ReadAllBytesAsync(manifestPath), Is.EqualTo(manifest));
        Assert.That(Directory.GetDirectories(root, ".Export-staging-*"), Is.Empty);
    }
}
