using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsDefaultExportPlannerTests
{
    [Test]
    public async Task NativeExportDoesNotDecodeOrPublishCachedAudio()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "Samples", "SkinDefault"); Directory.CreateDirectory(cache);
        try
        {
            var source = Path.Combine(root, "Song [Original].osu");
            await File.WriteAllTextAsync(source, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
            await File.WriteAllTextAsync(Path.Combine(cache, "normal-hitnormal.wav"), "must not decode this cache for native export");
            var project = new Hitbench.Core.Entities.ProjectData { AllowDefaults = true, Tracks = [new() { UsesDefaultSample = true, Samples = [new() { Timestamp = 100 }] }] };
            var decoded = false;
            var service = new OHsExportService(decode: (_, _) => { decoded = true; throw new IOException("unexpected decode"); });
            var output = await service.ExportAsync(new(Guid.NewGuid(), Path.Combine(root, "project.json"), source, "unused",
                Hitbench.Services.ProjectFiles.ProjectDataSerializer.Serialize(project), new("Hitsounds", 1, ".wav", ".wav", false)), default);
            Assert.That(decoded, Is.False);
            Assert.That(Directory.EnumerateFiles(output).Any(f => Path.GetExtension(f) == ".wav"), Is.False);
            var written = await File.ReadAllTextAsync(Path.Combine(output, "Song [Hitsounds].osu"));
            Assert.That(OHsExportBeatmap.Parse(written).TimingPoints.All(p => p.SampleIndex == 0), Is.True);
        }
        finally { Directory.Delete(root, true); }
    }
    private static OHsExportVoice Voice(int time, bool native) => new(time, native ? "SkinDefault/normal-hitnormal.wav" : "a.wav", 100, "Normal", SampleType.Normal, 0, native);
    private static OHsExportInput Input(params OHsExportVoice[] voices)
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        return new(new("Hitsounds", 1, ".ogg", ".ogg", false), map, voices, map.TimingPoints);
    }
    [Test]
    public void AllNativeGroupsUseBankZeroAndGenerateNoAssets()
    {
        var input = Input(Voice(100, true), Voice(200, true));
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.AudioFileCount, Is.Zero); Assert.That(plan.Banks, Is.Empty);
        Assert.That(plan.Circles.Select(c => c.SampleIndex), Is.EqualTo(new[] { 0, 0 }));
        Assert.That(plan.TimingPoints.All(p => p.SampleIndex == 0), Is.True);
        Assert.That(OHsExportBeatmapWriter.Write(input, plan), Does.Contain("0,500,4,1,0,100,1,0"));
    }
    [TestCase(200)]
    [TestCase(102)]
    public void NativeCustomNativeTransitionsPreserveOneObjectAndSources(int middle)
    {
        var input = Input(Voice(100, true), Voice(middle, true), Voice(middle, false), Voice(300, true));
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.Circles.Select(c => c.Timestamp), Is.EqualTo(new[] { 100, middle, 300 }));
        Assert.That(plan.Circles.Select(c => c.SampleIndex), Is.EqualTo(new[] { 0, 1, 0 }));
        foreach (var circle in plan.Circles)
            Assert.That(plan.TimingPoints.Last(p => p.Timestamp <= circle.Timestamp + 5).SampleIndex, Is.EqualTo(circle.SampleIndex));
        Assert.That(plan.Banks[1].Values.SelectMany(a => a.Sources).Select(s => s.FileName), Is.EquivalentTo(new[] { "SkinDefault/normal-hitnormal.wav", "a.wav" }));
    }
}
