using osuHitsounder.Core.Entities;
using osuHitsounder.Core.Enums;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
namespace osuHitsounder.Test.Services;

public class OHsExportBeatmapWriterTests
{
    [Test]
    public void StandardOutputPreservesTimingAndMetadataAndReplacesOriginalDependencies()
    {
        var source = OHsExportBeatmap.Parse("[General]\nAudioFilename: original.mp3\nMode:3\nStackLeniency:0.7\n[Metadata]\nTitle:Song\nVersion:Original\nBeatmapID:456\nBeatmapSetID:123\n[Difficulty]\nCircleSize:4\n[Events]\n0,0,\"bg.jpg\",0,0\nSample,100,0,\"sb.wav\",100\n2,500,600\n2,0,200\n[TimingPoints]\n0,428.571428571429,4,2,4,60,1,0\n50,-100,4,2,4,60,0,0\n80,-50,4,2,4,60,0,1\n[HitObjects]\n1,2,50,1,0,0:0:0:0:\n");
        var project = new ProjectData { Tracks = [new() { Addition = "Soft", SampleType = SampleType.Clap, Samples = [new() { FileName = "clap.wav", Timestamp = 100 }] }], ControlPoints = [new() { Timestamp = 0, Volume = 40 }, new() { Timestamp = 700, Volume = 40 }] };
        var input = OHsExportInputFactory.Create(project, source, new("Hitsounds", 1, ".ogg", ".ogg", false), default);
        var plan = OHsExportPlanner.Plan(input, default);
        var text = OHsExportBeatmapWriter.Write(input, plan, "song.mp3");
        var output = OHsExportBeatmap.Parse(text);
        Assert.That(output.Sections["General"], Does.Contain("Mode:0").And.Contain("StackLeniency:0").And.Contain("AudioFilename:song.mp3"));
        Assert.That(output.Sections["Metadata"], Does.Contain("Version:Hitsounds").And.Contain("BeatmapID:0").And.Contain("BeatmapSetID:123"));
        Assert.That(output.Sections["Events"].Where(l => !string.IsNullOrWhiteSpace(l)), Is.EqualTo(new[] { "2,500,600" }));
        Assert.That(output.Sections["HitObjects"].Where(l => !string.IsNullOrWhiteSpace(l)).Single().Split(',').Take(4), Is.EqualTo(new[] { "256", "192", "100", "1" }));
        Assert.That(output.TimingPoints.Any(p => p.Timestamp == 50), Is.False);
        Assert.That(output.TimingPoints[0].BeatLength, Is.EqualTo("428.571428571429"));
        var resolved = OHsExportPlaybackOracle.Resolve(text);
        Assert.That(resolved.Single(p => p.Type == "clap"), Is.EqualTo((100, 2, "clap", 1, 40)));
        Assert.That(resolved.All(p => plan.Banks[p.Index].ContainsKey(new(ControlPoint.SamplesetOptions[p.Set - 1], Enum.Parse<SampleType>(p.Type, true)))), Is.True);
    }
    [Test]
    public void DenseTimingResolvesEveryCircleToItsActualPlannedBank()
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var voices = new[] { new OHsExportVoice(1000, "a.wav", 70, "Normal", SampleType.Normal, 0), new OHsExportVoice(1002, "b.wav", 70, "Normal", SampleType.Normal, 0) };
        var input = new OHsExportInput(new("Hitsounds", 998, ".ogg", ".ogg", false), map, voices, map.TimingPoints);
        var plan = OHsExportPlanner.Plan(input, default);
        var resolved = OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan, "song.wav"));
        Assert.That(resolved.Select(p => p.Index), Is.EqualTo(plan.Circles.Select(c => c.SampleIndex)));
    }
}
