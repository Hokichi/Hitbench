using System.Diagnostics;
using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportShapingTests
{
    private static readonly OHsExportSlot Slot = new("Normal", SampleType.Normal);

    [Test]
    public void MixturePreparationResolvesEachComponentOnce()
    {
        using var a = DecodedAudio.CopyFrom([.2f, -.3f, .4f, .1f]);
        using var b = DecodedAudio.CopyFrom([.1f, .2f]);
        var recipe = Recipe(("a.wav", 70), ("b.wav", 50));
        var calls = new Dictionary<string, int>();
        var result = OHsExportRepresentations.Shape([recipe, recipe], 0, new([]), file =>
        {
            calls[file] = calls.GetValueOrDefault(file) + 1;
            return file == "a.wav" ? a : b;
        }, new Dictionary<string, OHsExportAsset>(), default);
        Assert.That(result, Is.Not.Empty);
        Assert.That(calls["a.wav"], Is.EqualTo(1));
        Assert.That(calls["b.wav"], Is.EqualTo(1));
    }

    [Test]
    public void NativeFactorsMatchPerSampleReference()
    {
        var random = new Random(512);
        ControlPointVolumeSnapshot[][] controls = [[], [new(0, 40)],
            [new(0, 10, IsRamp: true), new(1, 90), new(2, 20)],
            [new(0, 0, IsRamp: true), new(1, 100)]];
        foreach (var points in controls)
            for (var fixture = 0; fixture < 16; fixture++)
            {
                using var a = DecodedAudio.CopyFrom(Enumerable.Range(0, 240).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray());
                using var b = DecodedAudio.CopyFrom(Enumerable.Range(0, 100).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray());
                CheckReference(Recipe(("a.wav", fixture % 2 == 0 ? 200 : 30), ("b.wav", 70)), new(points), file => file == "a.wav" ? a : b);
            }
    }

    [TestCase(-1)]
    [TestCase(1)]
    public void StereoPeakAndToleranceBoundariesMatchReference(int sign)
    {
        foreach (var native in new[] { 1, 50, 99 })
            foreach (var excess in new[] { 0d, .9e-7, 1e-7, 1.1e-7 })
            {
                // Gain is applied before the fixed 50% envelope, allowing a precise double boundary.
                using var audio = DecodedAudio.CopyFrom([0, (float)(sign * (native / 100d + excess))]);
                CheckReference(Recipe(("a.wav", 200)), new([new(0, 50)]), _ => audio);
            }
    }

    [Test]
    public void NativeZeroRequiresAuthoredSilenceRatherThanCancellingComponents()
    {
        using var positive = DecodedAudio.CopyFrom([.4f, .4f]);
        using var negative = DecodedAudio.CopyFrom([-.4f, -.4f]);
        var recipe = Recipe(("positive.wav", 100), ("negative.wav", 100));
        var cancelled = OHsExportRepresentations.Shape([recipe], 0, new([]), file => file == "positive.wav" ? positive : negative, new Dictionary<string, OHsExportAsset>(), default);
        Assert.That(cancelled.Select(c => c.NativeVolume), Is.EquivalentTo(Enumerable.Range(1, 100)));
        var silent = OHsExportRepresentations.Shape([recipe], 0, new([new(0, 0)]), file => file == "positive.wav" ? positive : negative, new Dictionary<string, OHsExportAsset>(), default);
        Assert.That(silent.Select(c => c.NativeVolume), Is.EquivalentTo(Enumerable.Range(0, 101)));
        CheckReference(Recipe(("positive.wav", 0)), new([]), _ => positive);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancellationDoesNotDisposeBorrowedAudio(bool duringResolve)
    {
        using var audio = DecodedAudio.CopyFrom(new float[32768]);
        using var cancellation = new CancellationTokenSource();
        if (!duringResolve) cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => OHsExportRepresentations.Shape([Recipe(("a.wav", 100))], 0, new([]), _ =>
        {
            cancellation.Cancel();
            return audio;
        }, new Dictionary<string, OHsExportAsset>(), cancellation.Token));
        Assert.That(audio.FrameCount, Is.EqualTo(16384));
    }

    [Test]
    public void RampHeavyPreparationFinishesWithinCancellationBudget()
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Range(0, 176400).Select(i => (float)(.1 * Math.Sin(i * .07))).ToArray());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var timeline = new ControlPointVolumeTimeline([new(0, 10, IsRamp: true), new(10000, 80)]);
        var assets = new Dictionary<string, OHsExportAsset>();
        var recipe = Recipe(("a.wav", 100));
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 24; i++)
            Assert.That(OHsExportRepresentations.Shape([recipe], i * 100, timeline, _ => audio, assets, cancellation.Token), Is.Not.Empty);
        TestContext.Out.WriteLine($"24 distinct ramp offsets, 2s stereo PCM: {watch.Elapsed.TotalMilliseconds:F1} ms");
    }

    private static OHsExportRepresentation Recipe(params (string FileName, int GainPercent)[] sources) =>
        new("Normal", "Normal", 1, new Dictionary<OHsExportSlot, OHsExportAsset> { [Slot] = OHsExportAsset.Create(sources) }, 0, 0);

    private static void CheckReference(OHsExportRepresentation recipe, ControlPointVolumeTimeline timeline, Func<string, DecodedAudio> resolve)
    {
        var sources = recipe.Slots[Slot].Sources;
        var audio = sources.Select(s => resolve(s.FileName)).ToArray();
        var envelopes = audio.Select(a => OHsExportEnvelope.Create(timeline, 0, a.FrameCount)).ToArray();
        var expected = new List<int>();
        var silent = sources.Select((s, i) => s.GainPercent == 0 || envelopes[i].Segments.All(e => e.StartNumerator.IsZero && e.SlopeNumerator.IsZero)).All(s => s);
        for (var native = 0; native <= 100; native++)
        {
            var valid = native == 0 ? silent : true;
            for (var sample = 0; valid && native is > 0 and < 100 && sample < audio.Select(a => a.Samples.Length).DefaultIfEmpty().Max(); sample++)
            {
                double sum = 0;
                for (var j = 0; j < audio.Length; j++)
                    if (sample < audio[j].Samples.Length)
                        sum += audio[j].Samples[sample] * sources[j].GainPercent / 100d * envelopes[j].GetGain(sample / 2);
                valid = Math.Abs(Math.Clamp(sum, -1, 1) - Math.Clamp(sum * 100 / native, -1, 1) * native / 100) <= 1e-7;
            }
            if (valid) expected.Add(native);
        }
        var actual = OHsExportRepresentations.Shape([recipe], 0, timeline, resolve, new Dictionary<string, OHsExportAsset>(), default);
        Assert.That(actual.Select(c => c.NativeVolume), Is.EquivalentTo(expected));
        Assert.That(actual.All(c => c.IsLinear == expected.Contains(99)), Is.True);
        foreach (var choice in actual.Where(c => c.NativeVolume > 0))
        {
            var referenceAsset = OHsExportAsset.CreateShaped(sources.Select((s, i) => new OHsExportSource(s.FileName, s.GainPercent, envelopes[i].Scale(100, choice.NativeVolume))));
            Assert.That(choice.Slots[Slot].Id, Is.EqualTo(referenceAsset.Id));
        }
    }
}
