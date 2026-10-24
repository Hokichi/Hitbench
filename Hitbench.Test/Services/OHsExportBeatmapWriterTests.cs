using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
namespace Hitbench.Test.Services;

public class OHsExportBeatmapWriterTests
{
    public static string Template()
    {
        using var stream = typeof(OHsExportBeatmapWriter).Assembly.GetManifestResourceStream("Hitbench.Resources.OHsExportTemplate.osu")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static string OutsideBodies(string text) => Regex.Replace(text,
        @"(?m)^(\[(?:TimingPoints|HitObjects)\]\r?\n)[\s\S]*?(?=^\[|\z)", "$1");

    [TestCase(false), TestCase(true)]
    public void HitObjectSectionHasExactlyOneBlankLine(bool hasObjects)
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        OHsExportVoice[] voices = hasObjects ? [new(100, "a.wav", 100, "Soft", SampleType.Clap, 0)] : [];
        var input = new OHsExportInput(new("Hitsounds", 1, ".wav", ".wav", false), map, voices, map.TimingPoints);
        var text = OHsExportBeatmapWriter.Write(input, OHsExportPlanner.Plan(input, default));
        var lines = text.Split("\r\n");
        var header = Array.IndexOf(lines, "[HitObjects]");
        Assert.That(header, Is.GreaterThan(1));
        Assert.That(lines[header - 1], Is.Empty);
        Assert.That(lines[header - 2], Is.Not.Empty);
    }

    [Test]
    public void EmbeddedTemplateMatchesApprovedUpload()
    {
        using var stream = typeof(OHsExportBeatmapWriter).Assembly.GetManifestResourceStream("Hitbench.Resources.OHsExportTemplate.osu")!;
        Assert.That(Convert.ToHexString(SHA256.HashData(stream)), Is.EqualTo("1EFAB718F4E706AAB6B79BE4A8DB29DAC6B3880AAD2634DCC04E759C94BE77C6"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("original.mp3")]
    [TestCase("\"Audio/song name.mp3\"")]
    [TestCase("../outside.wav")]
    public void OnlyTimingAndHitObjectBodiesChange(string? audio)
    {
        var source = OHsExportBeatmap.Parse("[General]\n" + (audio is null ? "" : "AudioFilename: " + audio + "\n") + "[Metadata]\nTitle:Unrelated\nVersion:Original\n[TimingPoints]\n0,428.571428571429,4,2,4,60,1,0\n50,-100,4,2,4,60,0,0\n80,-50,4,2,4,60,0,1\n");
        var project = new ProjectData { Tracks = [new() { Addition = "Soft", SampleType = SampleType.Clap, Samples = [new() { FileName = "clap.wav", Timestamp = 100 }] }], ControlPoints = [new() { Timestamp = 0, Volume = 40 }, new() { Timestamp = 700, Volume = 40 }] };
        var input = OHsExportInputFactory.Create(project, source, new("Different name", 1, ".wav", ".wav", false), default);
        var plan = OHsExportPlanner.Plan(input, default);
        var text = OHsExportBeatmapWriter.Write(input, plan);
        Assert.That(OutsideBodies(text), Is.EqualTo(OutsideBodies(Template())));
        var output = OHsExportBeatmap.Parse(text);
        var objects = output.Sections["HitObjects"].Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(',')).ToArray();
        Assert.That(objects.Select(o => o[3]), Is.All.EqualTo("5"));
        Assert.That(objects.Select(o => int.Parse(o[4])), Is.EqualTo(plan.Circles.Select(c => c.HitSound)));
        Assert.That(output.TimingPoints[0].BeatLength, Is.EqualTo("428.571428571429"));
        Assert.That(output.TimingPoints.Any(p => p.Timestamp == 50), Is.False);
        Assert.That(output.Sections["HitObjects"].Count(l => !string.IsNullOrWhiteSpace(l)), Is.EqualTo(1));
        Assert.That(OHsExportPlaybackOracle.Resolve(text).Single(p => p.Type == "clap"), Is.EqualTo((100, 2, "clap", 1, 40)));
        Assert.That(text.Replace("\r\n", "").Contains('\n'), Is.False);
    }

    [Test]
    public void DuplicatePlanCirclesAreRejected()
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var input = new OHsExportInput(new("Hitsounds", 1, ".wav", ".wav", false), map, [new(100, "a.wav", 100, "Soft", SampleType.Clap, 0)], map.TimingPoints);
        var plan = OHsExportPlanner.Plan(input, default);
        var duplicate = new OHsExportPlan([plan.Circles[0], plan.Circles[0]], plan.TimingPoints, plan.Banks, plan.AdditionalPointCount);
        Assert.Throws<InvalidDataException>(() => OHsExportBeatmapWriter.Write(input, duplicate));
    }
    [Test]
    public void DenseTimingResolvesEveryCircleToItsActualPlannedBank()
    {
        var map = OHsExportBeatmap.Parse("[General]\nAudioFilename: song.wav\n[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var voices = new[] { new OHsExportVoice(1000, "a.wav", 70, "Normal", SampleType.Normal, 0), new OHsExportVoice(1002, "b.wav", 70, "Normal", SampleType.Normal, 0) };
        var input = new OHsExportInput(new("Hitsounds", 998, ".ogg", ".ogg", false), map, voices, map.TimingPoints);
        var plan = OHsExportPlanner.Plan(input, default);
        var text = OHsExportBeatmapWriter.Write(input, plan);
        var objects = OHsExportBeatmap.Parse(text).Sections["HitObjects"].Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(',')).ToArray();
        Assert.That(objects.Select(o => o[3]), Is.All.EqualTo("5"));
        Assert.That(objects.Select(o => int.Parse(o[4])), Is.EqualTo(plan.Circles.Select(c => c.HitSound)));
        var resolved = OHsExportPlaybackOracle.Resolve(text);
        Assert.That(resolved.Select(p => p.Index), Is.EqualTo(plan.Circles.Select(c => c.SampleIndex)));
        Assert.That(resolved.Select(p => plan.Banks[p.Index][new("Normal", SampleType.Normal)].Sources.Single().FileName),
            Is.EqualTo(new[] { "a.wav", "b.wav" }));
    }

    [Test]
    public void FirstPlayedLogicalBankGetsStartingIndex()
    {
        var voices = new[] { new OHsExportVoice(100, "a.wav", 100, "Normal", SampleType.Normal, 0), new OHsExportVoice(200, "b.wav", 100, "Normal", SampleType.Normal, 0) };
        var layout = OHsExportLayoutSearch.Find(voices.Select(v => OHsExportRepresentations.Build([v], default)).ToArray(), 2, default);
        var firstFile = layout[^1].Values.Single().Sources.Single().FileName;
        var reordered = voices.OrderByDescending(v => v.FileName == firstFile).Select((v, i) => v with { Timestamp = 100 + i * 100 }).ToArray();
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var input = new OHsExportInput(new("Hitsounds", 998, ".wav", ".wav", false), map, reordered, map.TimingPoints);
        var plan = OHsExportPlanner.Plan(input, default);
        var sounds = OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan));
        Assert.That(sounds.Select(s => s.Index), Is.EqualTo(new[] { 998, 999 }));
        Assert.That(sounds.Select(s => plan.Banks[s.Index][new("Normal", SampleType.Normal)].Sources.Single().FileName),
            Is.EqualTo(reordered.Select(v => v.FileName)));
    }

    [Test]
    public void MultipleEarlyBanksKeepDefaultsBeforeFirstRequiredRow()
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n500,500,4,2,7,40,1,0\n");
        var voices = new[] { new OHsExportVoice(100, "a.wav", 100, "Normal", SampleType.Normal, 0), new OHsExportVoice(200, "b.wav", 100, "Normal", SampleType.Normal, 0) };
        var input = new OHsExportInput(new("Hitsounds", 998, ".wav", ".wav", false), map, voices, map.TimingPoints);
        var plan = OHsExportPlanner.Plan(input, default);
        var written = OHsExportBeatmapWriter.Write(input, plan);
        var sounds = OHsExportPlaybackOracle.Resolve(written);
        Assert.That(sounds.Select(s => s.Index), Is.EqualTo(new[] { 998, 999 }));
        Assert.That(sounds.Select(s => s.Volume), Is.All.EqualTo(100));
        Assert.That(sounds.Select(s => plan.Banks[s.Index][new("Normal", SampleType.Normal)].Sources.Single().FileName), Is.EqualTo(new[] { "a.wav", "b.wav" }));
        var future = OHsExportBeatmap.Parse(written).TimingPoints.Single(p => p.Timestamp == 500);
        Assert.That((future.SampleSet, future.Volume, future.BeatLength), Is.EqualTo((2, 40, "500")));
    }

    [Test]
    public void RepeatedRequiredStatesAtDifferentTimestampsSurvive()
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var points = map.TimingPoints.Concat(new[] { 150, 250, 350 }.Select(t => new OHsExportTimingPoint(t, "-50", 3, 2, 1, 40, 1, OHsExportTimingKind.ProjectControl))).ToArray();
        var voices = new[] { 100, 200, 300, 400 }.Select((t, i) => new OHsExportVoice(t, i % 2 == 0 ? "a.wav" : "b.wav", 100, "Normal", SampleType.Normal, 0)).ToArray();
        var input = new OHsExportInput(new("Hitsounds", 1, ".wav", ".wav", false), map, voices, points);
        var plan = OHsExportPlanner.Plan(input, default);
        var written = OHsExportBeatmapWriter.Write(input, plan);
        var parsed = OHsExportBeatmap.Parse(written);
        foreach (var point in points)
        {
            var actual = parsed.TimingPoints.Single(p => p.Timestamp == point.Timestamp);
            Assert.That((actual.BeatLength, actual.Meter, actual.SampleSet, actual.Volume, actual.Effects),
                Is.EqualTo((point.BeatLength, point.Meter, point.SampleSet, point.Volume, point.Effects)));
        }
        Assert.That(parsed.Sections["HitObjects"].Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(',')[3]), Is.All.EqualTo("5"));
        Assert.That(plan.AudioFileCount, Is.EqualTo(2));
    }
}
