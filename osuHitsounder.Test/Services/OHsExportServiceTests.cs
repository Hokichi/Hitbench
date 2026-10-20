using osuHitsounder.Core.Entities;
using osuHitsounder.Core.Enums;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.Services.ProjectFiles;
namespace osuHitsounder.Test.Services;

public class OHsExportServiceTests
{
    [Test]
    public async Task PublishesStandaloneSongDifficultyAndChosenFormatsWithTrackGain()
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "source.osu"); var song = Path.Combine(root, "song.wav");
        await File.WriteAllTextAsync(source, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        await OHsExportAudio.WriteFileAsync(song, DecodedAudio.CopyFrom(new float[8820]), default);
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "a.wav"), DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 8820).ToArray()), default);
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "b.wav"), DecodedAudio.CopyFrom(Enumerable.Repeat(.1f, 8820).ToArray()), default);
        var project = new ProjectData { Tracks = [new() { Addition = "Soft", SampleType = SampleType.Clap, Volume = 70, Samples = [new() { FileName = "a.wav", Timestamp = 100 }, new() { FileName = "a.wav", Timestamp = 200 }] }, new() { Addition = "Soft", SampleType = SampleType.Clap, Volume = 40, Samples = [new() { FileName = "b.wav", Timestamp = 200 }] }], ControlPoints = [new() { Timestamp = 0, Volume = 40 }, new() { Timestamp = 500, Volume = 40 }] };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, song, ProjectDataSerializer.Serialize(project), new("Hitsounds", 998, ".wav", ".ogg", false));
        var output = await new OHsExportService().ExportAsync(request, default);
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(output, "__ohs_export_audio.wav")), Is.EqualTo(await File.ReadAllBytesAsync(song)));
        var text = await File.ReadAllTextAsync(Path.Combine(output, "Hitsounds.osu"));
        var resolved = OHsExportPlaybackOracle.Resolve(text).Where(p => p.Type == "clap").ToArray();
        Assert.That(resolved.Select(p => p.Volume), Is.All.EqualTo(40));
        var direct = AudioDecoder.Decode(Path.Combine(output, $"soft-hitclap{resolved[0].Index}.wav"), default);
        Assert.That(direct.Samples.ToArray(), Is.EqualTo(Enumerable.Repeat(.14f, 8820).ToArray()).Within(2d / 32768));
        var mixed = AudioDecoder.Decode(Path.Combine(output, $"soft-hitclap{resolved[1].Index}.ogg"), default);
        Assert.That(mixed.Samples.ToArray().Average(), Is.EqualTo(.18).Within(.025));
        Assert.That(await File.ReadAllTextAsync(source), Does.Contain("0,500,4,1,1,100,1,0"));
        Assert.That(OHsExportBeatmap.Parse(text).TimingPoints.Any(p => p.Timestamp == 500), Is.True);
        await new OHsExportService().ExportAsync(request with { Options = request.Options with { SingleFormat = ".ogg", MixedFormat = ".wav" } }, default);
        Assert.That(File.Exists(Path.Combine(output, $"soft-hitclap{resolved[0].Index}.wav")), Is.False);
    }
    [Test]
    public async Task MissingActiveSampleAndExhaustionPreservePreviousExport()
    {
        using var folder = new TestProjectFolder(); var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "source.osu"); var song = Path.Combine(root, "song.wav");
        await File.WriteAllTextAsync(source, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        await OHsExportAudio.WriteFileAsync(song, DecodedAudio.CopyFrom(new float[882]), default);
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, song, ProjectDataSerializer.Serialize(new ProjectData()), new("Hitsounds", 999, ".wav", ".wav", false));
        var exporter = new OHsExportService(); var output = await exporter.ExportAsync(request, default);
        var previous = await File.ReadAllTextAsync(Path.Combine(output, "Hitsounds.osu"));
        var project = new ProjectData { Tracks = [new() { Samples = [new() { FileName = "missing.wav", Timestamp = 100 }] }] };
        Assert.ThrowsAsync<FileNotFoundException>(() => exporter.ExportAsync(request with { ProjectJson = ProjectDataSerializer.Serialize(project) }, default));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "Hitsounds.osu")), Is.EqualTo(previous));
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "a.wav"), DecodedAudio.CopyFrom(new float[] { .2f, .2f }), default);
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "b.wav"), DecodedAudio.CopyFrom(new float[] { .1f, .1f }), default);
        project.Tracks[0].Samples = [new() { FileName = "a.wav", Timestamp = 100 }, new() { FileName = "b.wav", Timestamp = 200 }];
        Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync(request with { ProjectJson = ProjectDataSerializer.Serialize(project) }, default));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "Hitsounds.osu")), Is.EqualTo(previous));
    }
    [Test]
    public async Task ChangedSourceDuringDecodingRejectsStalePublication()
    {
        using var folder = new TestProjectFolder(); var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "source.osu"); var song = Path.Combine(root, "song.wav");
        await File.WriteAllTextAsync(source, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        await OHsExportAudio.WriteFileAsync(song, DecodedAudio.CopyFrom(new float[882]), default);
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "a.wav"), DecodedAudio.CopyFrom(new float[] { .2f, .2f }), default);
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, song, ProjectDataSerializer.Serialize(new ProjectData()), new("Hitsounds", 1, ".wav", ".wav", false));
        var output = await new OHsExportService().ExportAsync(request, default);
        var previous = await File.ReadAllTextAsync(Path.Combine(output, "Hitsounds.osu"));
        var exporter = new OHsExportService(decode: (path, token) => { File.AppendAllText(source, "// external edit\n"); return AudioDecoder.Decode(path, token); });
        var project = new ProjectData { Tracks = [new() { Volume = 70, Samples = [new() { FileName = "a.wav", Timestamp = 100 }] }] };
        Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(request with { ProjectJson = ProjectDataSerializer.Serialize(project) }, default));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "Hitsounds.osu")), Is.EqualTo(previous));
    }
    [Test]
    public async Task CorruptIncludedZeroGainSampleStillFailsDependencyValidation()
    {
        using var folder = new TestProjectFolder("corrupt.wav"); var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "source.osu"); var song = Path.Combine(root, "song.wav");
        await File.WriteAllTextAsync(source, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        await OHsExportAudio.WriteFileAsync(song, DecodedAudio.CopyFrom(new float[882]), default);
        var project = new ProjectData { Tracks = [new() { Volume = 0, Samples = [new() { FileName = "corrupt.wav", Timestamp = 100 }] }] };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, song, ProjectDataSerializer.Serialize(project), new("Hitsounds", 999, ".wav", ".wav", false));
        Assert.CatchAsync<Exception>(() => new OHsExportService().ExportAsync(request, default));
        Assert.That(Directory.Exists(Path.Combine(root, "Export")), Is.False);
    }
}
