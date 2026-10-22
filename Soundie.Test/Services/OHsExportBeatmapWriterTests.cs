using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Soundie.Core.Entities;
using Soundie.Core.Enums;
using Soundie.Resources.DataModels;
using Soundie.Services;
namespace Soundie.Test.Services;

public class OHsExportBeatmapWriterTests
{
    public static string Template()
    {
        using var stream = typeof(OHsExportBeatmapWriter).Assembly.GetManifestResourceStream("Soundie.Resources.OHsExportTemplate.osu")!;
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
        using var stream = typeof(OHsExportBeatmapWriter).Assembly.GetManifestResourceStream("Soundie.Resources.OHsExportTemplate.osu")!;
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
    }
}
