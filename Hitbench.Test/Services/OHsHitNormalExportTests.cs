using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;

namespace Hitbench.Test.Services;

public class OHsHitNormalExportTests
{
    [TestCase(false)] [TestCase(true)]
    public async Task ExplicitCachedSkinFallbackExportsRegardlessOfDefaultTrackGate(bool allowDefaults)
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var map = Path.Combine(root, "Song [Original].osu");
        await File.WriteAllTextAsync(map, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var skin = Directory.CreateDirectory(Path.Combine(root, "Samples", "SkinDefault"));
        using var audio = DecodedAudio.CopyFrom([.25f, .25f]);
        var fallback = Path.Combine(skin.FullName, "soft-hitclap.wav");
        await OHsExportAudio.WriteFileAsync(fallback, audio, default);
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "clap.wav"), audio, default);
        var project = new ProjectData { AllowDefaults = allowDefaults, NormalHitNormalPath = "SkinDefault/soft-hitclap.wav",
            Tracks = [new() { Addition = "Normal", SampleType = SampleType.Clap,
                Samples = [new() { FileName = "clap.wav", Timestamp = 100 }] }] };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, map, "unused.wav",
            ProjectDataSerializer.Serialize(project), new("Hitsounds", 2, ".wav", ".wav", false));
        var output = await new OHsExportService().ExportAsync(request, default);
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(output, "normal-hitnormal2.wav")),
            Is.EqualTo(await File.ReadAllBytesAsync(fallback)));
    }

    [Test]
    public void PlaybackOracleLayersAudibleNativeHitnormal()
    {
        var pcm = OHsExportPlaybackOracle.Render("[TimingPoints]\n0,500,4,1,2,100,1,0\n[HitObjects]\n256,192,0,5,8,1:1:0:0:\n",
            name => name.Contains("hitnormal") ? throw new FileNotFoundException() : DecodedAudio.CopyFrom([.2f, .2f]),
            1, _ => DecodedAudio.CopyFrom([.1f, .1f]));
        Assert.That(pcm, Is.EqualTo(new[] { .3f, .3f }).Within(.00001));
    }
    [TestCase(false, OHsExportDestination.Project, "Normal")][TestCase(true, OHsExportDestination.Project, "Normal")]
    [TestCase(false, OHsExportDestination.Beatmap, "Normal")][TestCase(true, OHsExportDestination.Beatmap, "Normal")]
    [TestCase(false, OHsExportDestination.Project, "Soft")][TestCase(true, OHsExportDestination.Project, "Soft")]
    [TestCase(false, OHsExportDestination.Beatmap, "Soft")][TestCase(true, OHsExportDestination.Beatmap, "Soft")]
    [TestCase(false, OHsExportDestination.Project, "Drum")][TestCase(true, OHsExportDestination.Project, "Drum")]
    [TestCase(false, OHsExportDestination.Beatmap, "Drum")][TestCase(true, OHsExportDestination.Beatmap, "Drum")]
    public async Task ExportsOnlyRequiredCustomHitnormal(bool custom, OHsExportDestination destination, string set)
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var mapset = Directory.CreateDirectory(Path.Combine(root, "mapset"));
        var map = Path.Combine(mapset.FullName, "Song [Original].osu");
        await File.WriteAllTextAsync(map, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.25f, 882).ToArray());
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "clap.wav"), audio, default);
        var normal = Path.Combine(mapset.FullName, set.ToLowerInvariant() + "-hitnormal.wav");
        await OHsExportAudio.WriteFileAsync(normal, audio, default);
        var project = new ProjectData { ControlPoints = [new() { Sampleset = set }], Tracks = [new() {
            Addition = "Normal", SampleType = SampleType.Clap, Samples = [new() { FileName = "clap.wav", Timestamp = 100 }]
        }] };
        if (custom) (set switch { "Soft" => new OHsHitNormalConfiguration(SoftPath: normal), "Drum" => new(DrumPath: normal), _ => new(normal) }).ApplyTo(project);
        var options = new OHsExportOptions("Hitsounds", 2, ".wav", ".wav", false) { Destination = destination };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, map, "unused.wav", ProjectDataSerializer.Serialize(project), options);
        var output = await new OHsExportService().ExportAsync(request, default);
        var outputNormal = Path.Combine(output, set.ToLowerInvariant() + "-hitnormal2.wav");
        Assert.That(File.Exists(outputNormal), Is.EqualTo(custom));
        if (custom) Assert.That(await File.ReadAllBytesAsync(outputNormal), Is.EqualTo(await File.ReadAllBytesAsync(normal)));
        Assert.That(File.Exists(Path.Combine(output, "normal-hitclap2.wav")), Is.True);
    }

    [TestCase(false)][TestCase(true)]
    public async Task RequiredMissingFallbackFailsButExplicitNormalOverridesIt(bool explicitNormal)
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var map = Path.Combine(root, "Song [Original].osu");
        await File.WriteAllTextAsync(map, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        using var audio = DecodedAudio.CopyFrom([.25f, .25f]);
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "hit.wav"), audio, default);
        var project = new ProjectData { NormalHitNormalPath = "missing.wav", Tracks = [new() { Addition = "Normal",
            SampleType = explicitNormal ? SampleType.Normal : SampleType.Clap, Samples = [new() { FileName = "hit.wav", Timestamp = 100 }]
        }] };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, map, "unused.wav", ProjectDataSerializer.Serialize(project), new("Hitsounds", 1, ".wav", ".wav", false));
        if (explicitNormal)
        {
            var output = await new OHsExportService().ExportAsync(request, default);
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(output, "normal-hitnormal.wav")), Is.EqualTo(await File.ReadAllBytesAsync(Path.Combine(root, "Samples", "hit.wav"))));
        }
        else
        {
            Assert.ThrowsAsync<FileNotFoundException>(() => new OHsExportService().ExportAsync(request, default));
            Assert.That(Directory.Exists(Path.Combine(root, "Export")), Is.False);
        }
    }

    [Test]
    public async Task ChangedMapsetSourceAbortsPublication()
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var map = Path.Combine(root, "Song [Original].osu");
        await File.WriteAllTextAsync(map, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var source = Path.Combine(root, "normal-hitnormal.wav");
        using var audio = DecodedAudio.CopyFrom([.1f, .1f]);
        await OHsExportAudio.WriteFileAsync(source, audio, default);
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "clap.wav"), audio, default);
        var project = new ProjectData { NormalHitNormalPath = source, Tracks = [new() { Addition = "Normal", SampleType = SampleType.Clap,
            Samples = [new() { FileName = "clap.wav", Timestamp = 100 }] }] };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, map, "unused", ProjectDataSerializer.Serialize(project), new("Hitsounds", 1, ".wav", ".wav", false));
        var changed = false;
        var progress = new InlineProgress<OHsExportProgress>(p => { if (!changed && p.Percent > 40) { changed = true; File.AppendAllText(source, "changed"); } });
        Assert.ThrowsAsync<IOException>(() => new OHsExportService().ExportAsync(request, default, progress));
        Assert.That(changed, Is.True); Assert.That(Directory.Exists(Path.Combine(root, "Export")), Is.False);
    }

    [Test]
    public async Task NativeMarkerIsNeverRendered()
    {
        using var folder = new TestProjectFolder();
        var stage = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(folder.ProjectPath)!, "stage"));
        var plan = new OHsExportPlan([], [], new Dictionary<int, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> {
            [1] = new Dictionary<OHsExportSlot, OHsExportAsset> { [new("Normal", SampleType.Normal)] = OHsExportAsset.NativeFallback }
        }, 0);
        await OHsExportAudio.WriteAsync(folder.ProjectPath, stage.FullName, plan, new("Hitsounds", 1, ".wav", ".wav", false), default,
            (_, _) => throw new AssertionException("Native fallback must not decode audio."));
        Assert.That(Directory.GetFiles(stage.FullName), Is.Empty);
    }
}
