using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportControlPointSeparationTests
{
    [TestCase(false), TestCase(true)]
    public void ControlPointVolumesAndSlopesNeverChangeAudioRecipes(bool instrumental)
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 882).ToArray());
        ControlPoint[][] controls = [[], [new() { Volume = 40 }], [new() { Volume = 0 }],
            [new() { Volume = 10, IsRamp = true }, new() { Timestamp = 2000, Volume = 80 }],
            [new() { Volume = 80, IsRamp = true }, new() { Timestamp = 2000, Volume = 20 }]];
        var expected = OHsExportAsset.Create([("a.wav", 70)]);
        foreach (var points in controls)
        {
            var input = Input(points, [100, 1100], instrumental, gain: 70);
            var plan = OHsExportPlanner.Plan(input, default, resolve: _ => audio);
            Assert.That(plan.AudioFileCount, Is.EqualTo(1));
            var asset = plan.Banks.Values.Single().Values.Single();
            Assert.That(asset.Id, Is.EqualTo(expected.Id));
            using var pcm = OHsExportAudio.Render(asset, _ => audio, default);
            Assert.That(pcm.Samples.ToArray().Max(v => Math.Abs(v - .14f)), Is.LessThan(1e-6));
        }
        var changed = OHsExportPlanner.Plan(Input([], [100], instrumental, gain: 50), default, resolve: _ => audio);
        using var quieter = OHsExportAudio.Render(changed.Banks.Values.Single().Values.Single(), _ => audio, default);
        Assert.That(quieter.Samples[0], Is.EqualTo(.1f).Within(1e-6));
    }

    [TestCase(false), TestCase(true)]
    public void NativeHitRetainsLookupGainThroughTail(bool instrumental)
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 176400).ToArray());
        var input = Input([new() { Volume = 0 }, new() { Timestamp = 100, Volume = 0, IsRamp = true },
            new() { Timestamp = 1000, Volume = 100 }], [0], instrumental);
        var plan = OHsExportPlanner.Plan(input, default, resolve: _ => audio);
        var asset = plan.Banks.Values.Single().Values.Single();
        using var pcm = OHsExportAudio.Render(asset, _ => audio, default);
        foreach (var index in new[] { 0, 44100, 132300, 176399 })
            Assert.That(pcm.Samples[index], Is.EqualTo(.2f));
        Assert.That(OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Single().Volume, Is.Zero);
        Assert.That(OHsExportAudio.IsPlaybackEquivalent(input, plan, _ => audio, default), Is.True);

        input = Input([new() { Volume = 10, IsRamp = true }, new() { Timestamp = 1000, Volume = 50 },
            new() { Timestamp = 1500, Volume = 20 }], [500], instrumental);
        plan = OHsExportPlanner.Plan(input, default, resolve: _ => audio);
        Assert.That(OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Single().Volume, Is.EqualTo(30));
        Assert.That(OHsExportAudio.IsPlaybackEquivalent(input, plan, _ => audio, default), Is.True);
    }

    [Test]
    public void RampTimingUsesRoundedNativeLookup()
    {
        var input = Input([new() { Volume = 10, IsRamp = true }, new() { Timestamp = 1000, Volume = 50 }], [100, 101, 500]);
        var volumeAt = OHsExportPlanner.CreateTimingVolumeResolver(input);
        Assert.That(volumeAt(125m), Is.EqualTo(15));
        Assert.That(volumeAt(112.5m), Is.EqualTo(15));
        Assert.That(volumeAt(505m), Is.EqualTo(30));
        var rows = OHsExportPlanner.BuildTimingPoints(input, [100, 101, 500], default);
        Assert.That(new[] { 105m, 505m }.Select(t => rows.Last(p => p.Timestamp <= t).Volume), Is.EqualTo(new[] { 14, 30 }));
        Assert.That(rows.Where(p => p.Kind == OHsExportTimingKind.ProjectControl).Select(p => p.Timestamp), Is.EqualTo(new decimal[] { 0, 1000 }));
        Assert.That(rows.Any(p => p.Timestamp == 106), Is.False, "Equal rounded ramp volumes reuse the preceding row.");
    }

    [Test]
    public void RequiredTimingRowsPreserveAuthoredState()
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,3,1,1,100,1,0\n250,-50,7,1,1,100,0,1\n");
        var input = Input([new() { Volume = 10, Sampleset = "Drum", IsRamp = true }, new() { Timestamp = 1000, Volume = 50 }], [100], map: map);
        var rows = OHsExportPlanner.BuildTimingPoints(input, [100], default);
        var source = rows.Single(p => p.Timestamp == 250);
        Assert.That((source.BeatLength, source.Meter, source.SampleSet, source.Effects, source.Volume), Is.EqualTo(("-50", 7, 3, 1, 20)));
        Assert.That(rows.Count(p => p.Timestamp == 0), Is.EqualTo(2));
    }

    [Test]
    public void DisabledDuplicateAndNegativeAnchorKeepEffectiveRamp()
    {
        var input = Input([new() { Timestamp = -1000, Volume = 10, IsRamp = true },
            new() { Timestamp = 0, Volume = 90 }, new() { Timestamp = 0, Volume = 99, IsDisabled = true },
            new() { Timestamp = 1000, Volume = 50 }], [0], offset: -1000);
        var volumeAt = OHsExportPlanner.CreateTimingVolumeResolver(input);
        Assert.That(volumeAt(0), Is.EqualTo(30));
        var rows = OHsExportPlanner.BuildTimingPoints(input, [0], default);
        Assert.That(rows.Single(p => p.Timestamp == -1000).Volume, Is.EqualTo(10));
        Assert.That(rows.Any(p => p.Timestamp == 0 && p.Kind == OHsExportTimingKind.ProjectControl), Is.False);
    }

    [Test]
    public void DenseLookupsRespectBoundaryInsideFiveMillisecondWindow()
    {
        var input = Input([new() { Volume = 40 }, new() { Timestamp = 1006, Volume = 80 }], [1000, 1002]);
        var rows = OHsExportPlanner.BuildTimingPoints(input, [1000, 1002], default);
        Assert.That(new[] { 1005m, 1007m }.Select(t => rows.Last(p => p.Timestamp <= t).Volume), Is.EqualTo(new[] { 40, 80 }));
        Assert.That(rows, Has.Length.EqualTo(input.RequiredPoints.Count));
    }

    [TestCase(false), TestCase(true)]
    public void DenseBankSwitchPreservesControlPointAndSourceState(bool decoded)
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n1004,-50,7,1,1,100,0,1\n");
        var baseline = Input([new() { Volume = 40 }, new() { Timestamp = 1006, Volume = 80, Sampleset = "Drum" }],
            [1000, 1002], map: map);
        var input = new OHsExportInput(baseline.Options, baseline.Beatmap,
            baseline.Voices.Select(v => v with { FileName = v.Timestamp == 1000 ? "a.wav" : "b.wav" }).ToArray(),
            baseline.RequiredPoints, baseline.ControlPoints);
        using var audio = DecodedAudio.CopyFrom([.2f, .2f]);
        var plan = OHsExportPlanner.Plan(input, default, resolve: decoded ? _ => audio : null);
        var row = plan.TimingPoints.Single(p => p.Timestamp == 1006);
        Assert.That((row.Volume, row.BeatLength, row.Meter, row.SampleSet, row.Effects), Is.EqualTo((80, "-50", 7, 3, 1)));
        Assert.That(plan.AdditionalPointCount, Is.Zero);
        Assert.That(plan.Circles[0].SampleIndex, Is.Not.EqualTo(plan.Circles[1].SampleIndex));
        Assert.That(OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Select(h => h.Volume), Is.EqualTo(new[] { 40, 80 }));
        Assert.That(OHsExportAudio.IsPlaybackEquivalent(input, plan, _ => audio, default), Is.True);
    }

    [Test]
    public void ExplicitRequiredVolumesAndCancellationWorkWithoutSnapshots()
    {
        var baseline = Input([], []);
        var input = new OHsExportInput(baseline.Options, baseline.Beatmap, [],
            [new(100, "-100", 4, 1, 1, 40, 0, OHsExportTimingKind.ProjectControl)]);
        var volumeAt = OHsExportPlanner.CreateTimingVolumeResolver(input);
        Assert.That(volumeAt(0), Is.EqualTo(100));
        Assert.That(volumeAt(100), Is.EqualTo(40));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Catch<OperationCanceledException>(() => OHsExportPlanner.BuildTimingPoints(input, [], cancellation.Token));
    }

    internal static OHsExportInput Input(ControlPoint[] controls, int[] times, bool instrumental = false, int gain = 100,
        int offset = 0, OHsExportBeatmap? map = null)
    {
        Track track = instrumental
            ? new InstrumentalTrack { KeyFiles = new() { ["C4"] = "a.wav" }, Notes = times.Select(t => new InstrumentalNote { Key = "C4", Timestamp = t }).ToList() }
            : new Track { Samples = times.Select(t => new Sample { FileName = "a.wav", Timestamp = t }).ToList() };
        track.Volume = gain; track.Addition = "Normal"; track.SampleType = SampleType.Normal;
        return OHsExportInputFactory.Create(new ProjectData { Offsets = [offset], BPMs = [120], ControlPoints = controls.ToList(), Tracks = [track] },
            map ?? OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n"), new("Hitsounds", 1, ".wav", ".wav", false), default);
    }
}
