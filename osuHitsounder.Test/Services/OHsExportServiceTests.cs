using osuHitsounder.Core.Entities;
using osuHitsounder.Core.Enums;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.Services.FileSystem;
using Newtonsoft.Json.Linq;
namespace osuHitsounder.Test.Services;

public class OHsExportServiceTests
{
    [Test]
    public async Task FirstAlphabeticalBeatmapSuppliesFilenameAndTimingWithFixedTemplate()
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var first = Path.Combine(root, "Artist [Live] - Song (Mapper) [Easy].osu");
        var later = Path.Combine(root, "Z Artist - Song (Mapper) [Hard].osu");
        const string original = "[General]\nAudioFilename: \"Audio/song name.mp3\"\n[Metadata]\nTitle:First\nVersion:Easy\n[TimingPoints]\n0,500,4,1,1,100,1,0\n";
        await File.WriteAllTextAsync(later, original.Replace("First", "Later").Replace("Audio/song name.mp3", "other.wav").Replace("0,500,", "0,600,"));
        await File.WriteAllTextAsync(first, original);
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, later, Path.Combine(root, "missing.wav"),
            ProjectDataSerializer.Serialize(new ProjectData()), new("Hitsounds", 1, ".wav", ".wav", false));
        var output = await new OHsExportService().ExportAsync(request, default);
        var text = await File.ReadAllTextAsync(Path.Combine(output, "Artist [Live] - Song (Mapper) [Hitsounds].osu"));
        var map = OHsExportBeatmap.Parse(text);
        Assert.That(map.Sections["General"], Is.EqualTo(OHsExportBeatmap.Parse(OHsExportBeatmapWriterTests.Template()).Sections["General"]));
        Assert.That(map.Sections["Metadata"], Is.EqualTo(OHsExportBeatmap.Parse(OHsExportBeatmapWriterTests.Template()).Sections["Metadata"]));
        Assert.That(map.TimingPoints[0].BeatLength, Is.EqualTo("500"));
        Assert.That(await File.ReadAllTextAsync(first), Is.EqualTo(original));
        Assert.That(await File.ReadAllTextAsync(later), Does.Contain("Title:Later"));
        var inventory = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(output, ".osuhitsounder-export.json")));
        Assert.That(inventory["files"]!.Select(f => (string?)f["role"]), Is.EqualTo(new[] { "difficulty" }));
        Assert.That(Directory.GetFiles(output).Select(Path.GetFileName), Is.EquivalentTo(new[]
            { "Artist [Live] - Song (Mapper) [Hitsounds].osu", ".osuhitsounder-export.json" }));
    }

    [TestCase("source.osu", "[General]\nAudioFilename: song.wav\n", "Hitsounds")]
    [TestCase("Song [Original.osu", "[General]\nAudioFilename: song.wav\n", "Hitsounds")]
    [TestCase("Song [Original].osu", "[General]\nAudioFilename: song.wav\n", null)]
    public async Task InvalidSourcePreservesPreviousPublication(string sourceName, string general, string? diffName)
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, sourceName);
        var song = Path.Combine(root, "song.wav");
        await File.WriteAllTextAsync(source, general + "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        await OHsExportAudio.WriteFileAsync(song, DecodedAudio.CopyFrom(new float[882]), default);
        var output = await new OHsExportPublisher().PublishAsync(folder.ProjectPath,
            new Dictionary<string, string> { ["previous.osu"] = "difficulty" },
            (stage, token) => File.WriteAllTextAsync(Path.Combine(stage, "previous.osu"), "previous", token), default);
        var manifest = await File.ReadAllTextAsync(Path.Combine(output, ".osuhitsounder-export.json"));
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, song,
            ProjectDataSerializer.Serialize(new ProjectData()), new(diffName ?? new string('x', 251), 1, ".wav", ".wav", false));
        Assert.CatchAsync<Exception>(() => new OHsExportService().ExportAsync(request, default));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "previous.osu")), Is.EqualTo("previous"));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, ".osuhitsounder-export.json")), Is.EqualTo(manifest));
        Assert.That(Directory.GetDirectories(root, ".Export-staging-*"), Is.Empty);
    }

    [Test]
    public async Task ReexportRemovesLegacyOwnedSongAndPreservesUnrelatedFiles()
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "Song [Original].osu");
        var song = Path.Combine(root, "song.wav");
        await File.WriteAllTextAsync(source, "[General]\nAudioFilename: song.wav\n[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        await OHsExportAudio.WriteFileAsync(song, DecodedAudio.CopyFrom(new float[882]), default);
        var output = await new OHsExportPublisher().PublishAsync(folder.ProjectPath,
            new Dictionary<string, string> { ["__ohs_export_audio.wav"] = "song" },
            (stage, token) => File.WriteAllTextAsync(Path.Combine(stage, "__ohs_export_audio.wav"), "legacy", token), default);
        await File.WriteAllTextAsync(Path.Combine(output, "keep.txt"), "user file");
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, song,
            ProjectDataSerializer.Serialize(new ProjectData()), new("Hitsounds", 1, ".wav", ".wav", false));
        await new OHsExportService().ExportAsync(request, default);
        Assert.That(File.Exists(Path.Combine(output, "__ohs_export_audio.wav")), Is.False);
        Assert.That(File.Exists(Path.Combine(output, "song.wav")), Is.False);
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "keep.txt")), Is.EqualTo("user file"));
        var manifest = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(output, ".osuhitsounder-export.json")));
        Assert.That(manifest["files"]!.Select(f => (string?)f["path"]), Is.EqualTo(new[] { "Song [Hitsounds].osu" }));
    }

    [TestCase(".wav"), TestCase(".ogg")]
    public async Task OverlappingFullVolumeSamplesPublishWithCappedPeaks(string format)
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "Artist - Song (Mapper) [Original].osu");
        var song = Path.Combine(root, "song.wav");
        var first = Path.Combine(root, "Samples", "ding04.wav");
        var second = Path.Combine(root, "Samples", "soft-hitfinish3.wav");
        await File.WriteAllTextAsync(source, "[General]\nAudioFilename: song.wav\n[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        await OHsExportAudio.WriteFileAsync(song, DecodedAudio.CopyFrom(new float[44100]), default);
        await OHsExportAudio.WriteFileAsync(first,
            DecodedAudio.CopyFrom(Enumerable.Range(0, 4410).SelectMany(_ => new[] { .8f, -.8f }).ToArray()), default);
        await OHsExportAudio.WriteFileAsync(second,
            DecodedAudio.CopyFrom(Enumerable.Range(0, 4410).SelectMany(_ => new[] { .7f, -.7f }).ToArray()), default);
        var originals = new Dictionary<string, byte[]>();
        foreach (var path in new[] { source, song, first, second }) originals.Add(path, await File.ReadAllBytesAsync(path));
        var project = new ProjectData
        {
            Tracks =
            [
                new() { Addition = "Soft", SampleType = SampleType.Clap, Volume = 100,
                    Samples = [new() { FileName = "ding04.wav", Timestamp = 100 }] },
                new() { Addition = "Soft", SampleType = SampleType.Clap, Volume = 100,
                    Samples = [new() { FileName = "soft-hitfinish3.wav", Timestamp = 100 }] }
            ]
        };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, song,
            ProjectDataSerializer.Serialize(project), new("Hitsounds", 1, format, format, false));
        var exporter = new OHsExportService();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var output = await exporter.ExportAsync(request, default);
            Assert.That(output, Is.EqualTo(Path.Combine(root, "Export")));
            Assert.That(File.Exists(Path.Combine(output, ".osuhitsounder-export.json")), Is.True);
            var text = await File.ReadAllTextAsync(Path.Combine(output, "Artist - Song (Mapper) [Hitsounds].osu"));
            Assert.That(OHsExportBeatmap.Parse(text).Sections["HitObjects"].Count(l => !string.IsNullOrWhiteSpace(l)), Is.EqualTo(1));
            var clap = OHsExportPlaybackOracle.Resolve(text).Single(p => p.Type == "clap");
            Assert.That((clap.Timestamp, clap.Set, clap.Index), Is.EqualTo((100, 2, 1)));
            var suffix = clap.Index == 1 ? "" : clap.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var audio = AudioDecoder.Decode(Path.Combine(output, $"soft-hitclap{suffix}{format}"), default);
            var samples = audio.Samples.ToArray();
            Assert.That(audio.FrameCount, Is.EqualTo(4410));
            Assert.That(samples.All(float.IsFinite), Is.True);
            if (format == ".wav")
                Assert.That(samples, Is.EqualTo(Enumerable.Range(0, 4410).SelectMany(_ => new[] { 1f, -1f }).ToArray()).Within(2d / 32768));
            else Assert.That(samples.Average(v => Math.Abs(v)), Is.GreaterThan(.5));
            Assert.That(File.Exists(Path.Combine(output, "__ohs_export_audio.wav")), Is.False);
            foreach (var original in originals)
                Assert.That(await File.ReadAllBytesAsync(original.Key), Is.EqualTo(original.Value));
        }
    }

    [TestCase(1), TestCase(999)]
    public async Task LargeRepeatedArrangementPublishesOneCenteredCirclePerTimestamp(int firstIndex)
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "Artist - Song (Mapper) [Original].osu"); var song = Path.Combine(root, "song.wav");
        await File.WriteAllTextAsync(source, "[General]\nAudioFilename: song.wav\n[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        await OHsExportAudio.WriteFileAsync(song, DecodedAudio.CopyFrom(new float[882]), default);
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "a.wav"), DecodedAudio.CopyFrom(new float[] { .2f, -.2f }), default);
        var project = new ProjectData { Tracks = [new() { Addition = "Soft", SampleType = SampleType.Clap, Volume = 70,
            Samples = Enumerable.Range(1, 500).Select(i => new Sample { FileName = "a.wav", Timestamp = i * 20 }).ToList() },
            new() { IsTrackMuted = true, Samples = [new() { FileName = "missing.wav", Timestamp = 1 }] }],
            ControlPoints = [new() { Timestamp = 0, Volume = 40 }, new() { Timestamp = 5000, Volume = 60 }] };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, song, ProjectDataSerializer.Serialize(project), new("Hitsounds", firstIndex, ".wav", ".wav", false));
        var exporter = new OHsExportService();
        var output = await exporter.ExportAsync(request, default);
        var text = await File.ReadAllTextAsync(Path.Combine(output, "Artist - Song (Mapper) [Hitsounds].osu"));
        var parsed = OHsExportBeatmap.Parse(text);
        var objects = parsed.Sections["HitObjects"].Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(',')).ToArray();
        Assert.That(objects, Has.Length.EqualTo(500));
        Assert.That(objects.Select(o => (o[0], o[1])), Is.All.EqualTo(("256", "192")));
        Assert.That(objects.Select(o => o[2]).Distinct().Count(), Is.EqualTo(500));
        Assert.That(parsed.Sections["General"], Is.EqualTo(OHsExportBeatmap.Parse(OHsExportBeatmapWriterTests.Template()).Sections["General"]));
        var resolved = OHsExportPlaybackOracle.Resolve(text).Where(p => p.Type == "clap").ToArray();
        Assert.That(resolved.Select(p => p.Index), Is.All.EqualTo(firstIndex));
        Assert.That(resolved.Where(p => p.Timestamp < 4995).Select(p => p.Volume), Is.All.EqualTo(40));
        Assert.That(resolved.Where(p => p.Timestamp >= 5000).Select(p => p.Volume), Is.All.EqualTo(60));
        foreach (var file in Directory.GetFiles(output, "*.wav")) Assert.That(AudioDecoder.Decode(file, default).FrameCount, Is.GreaterThan(0));
        await exporter.ExportAsync(request, default);
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "Artist - Song (Mapper) [Hitsounds].osu")), Is.EqualTo(text));
    }
    [Test]
    public async Task PublishesDifficultyAndChosenSampleFormatsWithTrackGain()
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "Artist - Song (Mapper) [Original].osu"); var song = Path.Combine(root, "song.wav");
        await File.WriteAllTextAsync(source, "[General]\nAudioFilename: song.wav\n[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        await OHsExportAudio.WriteFileAsync(song, DecodedAudio.CopyFrom(new float[8820]), default);
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "a.wav"), DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 8820).ToArray()), default);
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "b.wav"), DecodedAudio.CopyFrom(Enumerable.Repeat(.1f, 8820).ToArray()), default);
        var project = new ProjectData { Tracks = [new() { Addition = "Soft", SampleType = SampleType.Clap, Volume = 70, Samples = [new() { FileName = "a.wav", Timestamp = 100 }, new() { FileName = "a.wav", Timestamp = 200 }] }, new() { Addition = "Soft", SampleType = SampleType.Clap, Volume = 40, Samples = [new() { FileName = "b.wav", Timestamp = 200 }] }], ControlPoints = [new() { Timestamp = 0, Volume = 40 }, new() { Timestamp = 500, Volume = 40 }] };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, song, ProjectDataSerializer.Serialize(project), new("Hitsounds", 998, ".wav", ".ogg", false));
        var output = await new OHsExportService().ExportAsync(request, default);
        Assert.That(File.Exists(Path.Combine(output, "__ohs_export_audio.wav")), Is.False);
        var text = await File.ReadAllTextAsync(Path.Combine(output, "Artist - Song (Mapper) [Hitsounds].osu"));
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
        var source = Path.Combine(root, "Artist - Song (Mapper) [Original].osu"); var song = Path.Combine(root, "song.wav");
        await File.WriteAllTextAsync(source, "[General]\nAudioFilename: song.wav\n[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        await OHsExportAudio.WriteFileAsync(song, DecodedAudio.CopyFrom(new float[882]), default);
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, song, ProjectDataSerializer.Serialize(new ProjectData()), new("Hitsounds", 999, ".wav", ".wav", false));
        var exporter = new OHsExportService(); var output = await exporter.ExportAsync(request, default);
        var previous = await File.ReadAllTextAsync(Path.Combine(output, "Artist - Song (Mapper) [Hitsounds].osu"));
        var project = new ProjectData { Tracks = [new() { Samples = [new() { FileName = "missing.wav", Timestamp = 100 }] }] };
        Assert.ThrowsAsync<FileNotFoundException>(() => exporter.ExportAsync(request with { ProjectJson = ProjectDataSerializer.Serialize(project) }, default));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "Artist - Song (Mapper) [Hitsounds].osu")), Is.EqualTo(previous));
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "a.wav"), DecodedAudio.CopyFrom(new float[] { .2f, .2f }), default);
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "b.wav"), DecodedAudio.CopyFrom(new float[] { .1f, .1f }), default);
        project.Tracks[0].Samples = [new() { FileName = "a.wav", Timestamp = 100 }, new() { FileName = "b.wav", Timestamp = 200 }];
        Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync(request with { ProjectJson = ProjectDataSerializer.Serialize(project) }, default));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "Artist - Song (Mapper) [Hitsounds].osu")), Is.EqualTo(previous));
    }
    [Test]
    public async Task ChangedSourceDuringDecodingRejectsStalePublication()
    {
        using var folder = new TestProjectFolder(); var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "Artist - Song (Mapper) [Original].osu"); var song = Path.Combine(root, "song.wav");
        await File.WriteAllTextAsync(source, "[General]\nAudioFilename: song.wav\n[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        await OHsExportAudio.WriteFileAsync(song, DecodedAudio.CopyFrom(new float[882]), default);
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "a.wav"), DecodedAudio.CopyFrom(new float[] { .2f, .2f }), default);
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, song, ProjectDataSerializer.Serialize(new ProjectData()), new("Hitsounds", 1, ".wav", ".wav", false));
        var output = await new OHsExportService().ExportAsync(request, default);
        var previous = await File.ReadAllTextAsync(Path.Combine(output, "Artist - Song (Mapper) [Hitsounds].osu"));
        var exporter = new OHsExportService(decode: (path, token) => { File.AppendAllText(source, "// external edit\n"); return AudioDecoder.Decode(path, token); });
        var project = new ProjectData { Tracks = [new() { Volume = 70, Samples = [new() { FileName = "a.wav", Timestamp = 100 }] }] };
        Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(request with { ProjectJson = ProjectDataSerializer.Serialize(project) }, default));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "Artist - Song (Mapper) [Hitsounds].osu")), Is.EqualTo(previous));
    }
    [Test]
    public async Task CorruptIncludedZeroGainSampleStillFailsDependencyValidation()
    {
        using var folder = new TestProjectFolder("corrupt.wav"); var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "Artist - Song (Mapper) [Original].osu"); var song = Path.Combine(root, "song.wav");
        await File.WriteAllTextAsync(source, "[General]\nAudioFilename: song.wav\n[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        await OHsExportAudio.WriteFileAsync(song, DecodedAudio.CopyFrom(new float[882]), default);
        var project = new ProjectData { Tracks = [new() { Volume = 0, Samples = [new() { FileName = "corrupt.wav", Timestamp = 100 }] }] };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, song, ProjectDataSerializer.Serialize(project), new("Hitsounds", 999, ".wav", ".wav", false));
        Assert.CatchAsync<Exception>(() => new OHsExportService().ExportAsync(request, default));
        Assert.That(Directory.Exists(Path.Combine(root, "Export")), Is.False);
    }

    [Test]
    public async Task SimultaneousOrdinaryAndInstrumentalVoicesPreserveAllAudio()
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var source = Path.Combine(root, "Song [Original].osu");
        await File.WriteAllTextAsync(source, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        foreach (var (name, value) in new[] { ("repeat.wav", .10f), ("normal.wav", .20f), ("finish.wav", .05f), ("c.wav", .30f), ("e.wav", .40f) })
            await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", name), DecodedAudio.CopyFrom(Enumerable.Repeat(value, 882).ToArray()), default);
        Track Ordinary(string file, int gain, string set, SampleType type) => new() { Addition = set, SampleType = type, Volume = gain, Samples = [new() { FileName = file, Timestamp = 100 }] };
        var project = new ProjectData { ControlPoints = [new() { Timestamp = 0, Volume = 100 }], Tracks =
        [
            Ordinary("repeat.wav", 50, "Soft", SampleType.Clap),
            Ordinary("repeat.wav", 50, "Soft", SampleType.Clap),
            Ordinary("normal.wav", 100, "Normal", SampleType.Normal),
            Ordinary("finish.wav", 100, "Drum", SampleType.Finish),
            new InstrumentalTrack { Volume = 50, KeyFiles = new() { ["C4"] = "c.wav", ["E4"] = "e.wav" }, Notes = [new() { Key = "C4", Timestamp = 100 }, new() { Key = "E4", Timestamp = 100 }] },
            new() { IsTrackMuted = true, Samples = [new() { FileName = "missing.wav", Timestamp = 100 }] }
        ] };
        var originals = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, source, "missing-song.wav", ProjectDataSerializer.Serialize(project), new("Different name", 1, ".wav", ".wav", false));
        var service = new OHsExportService();
        var output = await service.ExportAsync(request, default);
        var mapPath = Path.Combine(output, "Song [Different name].osu");
        var text = await File.ReadAllTextAsync(mapPath);
        Assert.That(OHsExportBeatmap.Parse(text).Sections["HitObjects"].Count(l => !string.IsNullOrWhiteSpace(l)), Is.EqualTo(1));
        var sum = new float[882];
        foreach (var sound in OHsExportPlaybackOracle.Resolve(text))
        {
            var slot = new OHsExportSlot(new[] { "Normal", "Soft", "Drum" }[sound.Set - 1], Enum.Parse<SampleType>(sound.Type, true));
            var pcm = AudioDecoder.Decode(Path.Combine(output, OHsExportRepresentations.FileName(slot, sound.Index, ".wav")), default).Samples.ToArray();
            for (var i = 0; i < sum.Length; i++) sum[i] += pcm[i];
        }
        Assert.That(sum.All(value => Math.Abs(value - .70f) <= 8d / 32768), Is.True);
        var published = Directory.GetFiles(output).ToDictionary(f => f, File.ReadAllBytes);
        await service.ExportAsync(request, default);
        foreach (var file in published) Assert.That(await File.ReadAllBytesAsync(file.Key), Is.EqualTo(file.Value));
        foreach (var file in originals) Assert.That(await File.ReadAllBytesAsync(file.Key), Is.EqualTo(file.Value));

        project.Tracks = [new() { Addition = "Soft", SampleType = SampleType.Clap, Samples = [new() { FileName = "repeat.wav", Timestamp = 100 }, new() { FileName = "normal.wav", Timestamp = 101 }] }];
        await service.ExportAsync(request with { ProjectJson = ProjectDataSerializer.Serialize(project) }, default);
        var neighbours = OHsExportPlaybackOracle.Resolve(await File.ReadAllTextAsync(mapPath)).Where(p => p.Type == "clap").ToArray();
        Assert.That(neighbours.Select(p => p.Timestamp), Is.EqualTo(new[] { 100, 101 }));
        foreach (var sound in neighbours)
        {
            var pcm = AudioDecoder.Decode(Path.Combine(output, OHsExportRepresentations.FileName(new("Soft", SampleType.Clap), sound.Index, ".wav")), default);
            Assert.That(pcm.Samples.ToArray().All(value => Math.Abs(value - (sound.Timestamp == 100 ? .10f : .20f)) <= 2d / 32768), Is.True);
        }
    }
}
