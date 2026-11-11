using Hitbench.Test;
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
            var plan = OHsExportPlanner.Plan(input, default);
            Assert.That(plan.AudioFileCount, Is.EqualTo(1));
            var asset = plan.Banks.Values.Single().Values.Single();
            Assert.That(asset.Id, Is.EqualTo(expected.Id));
            using var pcm = OHsExportAudio.Render(asset, _ => audio, default);
            Assert.That(pcm.Samples.ToArray().Max(v => Math.Abs(v - .14f)), Is.LessThan(1e-6));
        }
        var changed = OHsExportPlanner.Plan(Input([], [100], instrumental, gain: 50), default);
        using var quieter = OHsExportAudio.Render(changed.Banks.Values.Single().Values.Single(), _ => audio, default);
        Assert.That(quieter.Samples[0], Is.EqualTo(.1f).Within(1e-6));
    }

    [TestCase(false), TestCase(true)]
    public void NativeHitRetainsLookupGainThroughTail(bool instrumental)
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 176400).ToArray());
        var input = Input([new() { Volume = 0 }, new() { Timestamp = 100, Volume = 0, IsRamp = true },
            new() { Timestamp = 1000, Volume = 100 }], [0], instrumental);
        var plan = OHsExportPlanner.Plan(input, default);
        var asset = plan.Banks.Values.Single().Values.Single();
        using var pcm = OHsExportAudio.Render(asset, _ => audio, default);
        foreach (var index in new[] { 0, 44100, 132300, 176399 })
            Assert.That(pcm.Samples[index], Is.EqualTo(.2f));
        Assert.That(OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Single().Volume, Is.Zero);

        input = Input([new() { Volume = 10, IsRamp = true }, new() { Timestamp = 1000, Volume = 50 },
            new() { Timestamp = 1500, Volume = 20 }], [500], instrumental);
        plan = OHsExportPlanner.Plan(input, default);
        Assert.That(OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Single().Volume, Is.EqualTo(30));
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
        Assert.That(rows.Where(p => p.Kind == OHsExportTimingKind.BankSwitch).Select(p => p.Timestamp), Is.EqualTo(new[] { 100m, 500m }));
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

    [Test]
    public void DenseBankSwitchPreservesControlPointAndSourceState()
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n1004,-50,7,1,1,100,0,1\n");
        var baseline = Input([new() { Volume = 40 }, new() { Timestamp = 1006, Volume = 80, Sampleset = "Drum" }],
            [1000, 1002], map: map);
        var input = new OHsExportInput(baseline.Options, baseline.Beatmap,
            baseline.Voices.Select(v => v with { FileName = v.Timestamp == 1000 ? "a.wav" : "b.wav" }).ToArray(),
            baseline.RequiredPoints, baseline.ControlPoints);
        using var audio = DecodedAudio.CopyFrom([.2f, .2f]);
        var plan = OHsExportPlanner.Plan(input, default);
        var row = plan.TimingPoints.Single(p => p.Timestamp == 1006);
        Assert.That((row.Volume, row.BeatLength, row.Meter, row.SampleSet, row.Effects), Is.EqualTo((80, "-50", 7, 3, 1)));
        Assert.That(plan.AdditionalPointCount, Is.Zero);
        Assert.That(plan.Circles[0].SampleIndex, Is.Not.EqualTo(plan.Circles[1].SampleIndex));
        Assert.That(OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Select(h => h.Volume), Is.EqualTo(new[] { 40, 80 }));
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

    [Test]
    public void DenseRequiredVolumeSwitchKeepsOneFile()
    {
        var input = Input([new() { Volume = 100 }, new() { Timestamp = 1006, Volume = 50 }], [1000, 1002]);
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.AudioFileCount, Is.EqualTo(1));
        Assert.That(OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Select(h => h.Volume), Is.EqualTo(new[] { 100, 50 }));
    }

    [TestCase(false), TestCase(true)]
    public void SpacedRampRowsAlignWithoutChangingLookupGain(bool falling)
    {
        var input = Input([new() { Volume = falling ? 100 : 0, IsRamp = true }, new() { Timestamp = 100, Volume = falling ? 0 : 100 }], [0, 10, 50]);
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.TimingPoints.Where(p => p.Kind == OHsExportTimingKind.BankSwitch).Select(p => p.Timestamp), Is.EqualTo(new[] { 0m, 10m, 50m }));
        Assert.That(OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Select(h => h.Volume),
            Is.EqualTo(falling ? new[] { 95, 85, 45 } : new[] { 5, 15, 55 }));
    }

    [TestCase(2), TestCase(5), TestCase(6)]
    public void RampAlignmentRespectsInclusiveEarlierLookup(int gap)
    {
        var input = Input([new() { Volume = 0, IsRamp = true }, new() { Timestamp = 100, Volume = 100 }], [10, 10 + gap]);
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.TimingPoints.Where(p => p.Kind == OHsExportTimingKind.BankSwitch).Select(p => p.Timestamp),
            Is.EqualTo(new[] { 10m, gap <= 5 ? 15m + gap : 10m + gap }));
        Assert.That(OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Select(h => h.Volume), Is.EqualTo(new[] { 15, 15 + gap }));
    }

    [Test]
    public void EarlierHitWithoutNewRowStillProtectsItsLookup()
    {
        var input = Input([new() { Timestamp = 9, Volume = 1, IsRamp = true }, new() { Timestamp = 1000, Volume = 100 }], [9, 11]);
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.TimingPoints.Single(p => p.Kind == OHsExportTimingKind.BankSwitch).Timestamp, Is.EqualTo(16m));
        Assert.That(OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Select(h => h.Volume), Is.EqualTo(new[] { 1, 2 }));
    }

    [TestCase(0), TestCase(3), TestCase(5), TestCase(6)]
    public void RampAlignmentDoesNotAnticipateSourceTiming(int offset)
    {
        var boundary = 100 + offset;
        var map = OHsExportBeatmap.Parse($"[TimingPoints]\n0,500,4,1,1,100,1,0\n{boundary},-50,7,1,1,100,0,1\n");
        var input = Input([new() { Volume = 0, IsRamp = true }, new() { Timestamp = 1000, Volume = 100 }], [100], map: map);
        var plan = OHsExportPlanner.Plan(input, default);
        var row = plan.TimingPoints.SingleOrDefault(p => p.Kind == OHsExportTimingKind.BankSwitch);
        if (offset == 5) Assert.That(row, Is.Null, "The authored row already supplies the lookup volume.");
        else
        {
            Assert.That(row!.Timestamp, Is.EqualTo(offset == 3 ? 105m : 100m));
            Assert.That((row.BeatLength, row.Effects), Is.EqualTo(offset <= 5 ? ("-50", 1) : ("-100", 0)));
        }
        Assert.That(plan.TimingPoints.Single(p => p.Kind == OHsExportTimingKind.SourceState).Timestamp, Is.EqualTo(boundary));
        Assert.That(OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Single().Volume, Is.EqualTo(11));
    }

    [TestCase(2), TestCase(100)]
    public void AlignedRampRowsPreserveBankChanges(int gap)
    {
        var baseline = Input([new() { Volume = 0, IsRamp = true }, new() { Timestamp = 1000, Volume = 100 }], [100, 100 + gap]);
        var input = new OHsExportInput(baseline.Options, baseline.Beatmap, baseline.Voices.Select((v, i) => v with { FileName = i == 0 ? "a.wav" : "b.wav" }).ToArray(), baseline.RequiredPoints, baseline.ControlPoints);
        var plan = OHsExportPlanner.Plan(input, default);
        var hits = OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan));
        Assert.That(hits.Select(h => h.Index), Is.EqualTo(plan.Circles.Select(c => c.SampleIndex)));
        Assert.That(hits.Select(h => h.Volume), Is.EqualTo(gap == 2 ? new[] { 11, 11 } : new[] { 11, 21 }));
        Assert.That(hits.Select(h => plan.Banks[h.Index].Values.Single().Sources.Single().FileName), Is.EqualTo(new[] { "a.wav", "b.wav" }));
    }

    [Test]
    public void RampNearMaximumTimestampAlignsWithoutOverflow()
    {
        var input = Input([new() { Timestamp = int.MaxValue - 100, Volume = 0, IsRamp = true }, new() { Timestamp = int.MaxValue, Volume = 100 }], [int.MaxValue - 10]);
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.TimingPoints.Single(p => p.Kind == OHsExportTimingKind.BankSwitch).Timestamp, Is.EqualTo((decimal)int.MaxValue - 10));
        Assert.That(OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Single().Volume, Is.EqualTo(95));
    }

    internal static OHsExportInput Input(ControlPoint[] controls, int[] times, bool instrumental = false, int gain = 100,
        int offset = 0, OHsExportBeatmap? map = null)
    {
        Track track = instrumental
            ? new InstrumentalTrack { KeyFiles = new() { ["C4"] = "a.wav" }, Notes = times.Select(t => new InstrumentalNote { Key = "C4", Timestamp = t }).ToList() }
            : new Track { Samples = times.Select(t => new Sample { FileName = "a.wav", Timestamp = t }).ToList() };
        track.Volume = gain; track.Addition = "Normal"; track.SampleType = SampleType.Normal;
        return OHsExportInputFactory.Create(new ProjectData { UninheritedPoints = TimingTestData.Points([offset], [120]), ControlPoints = controls.ToList(), Tracks = [track] },
            map ?? OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n"), new("Hitsounds", 1, ".wav", ".wav", false), default);
    }
}
