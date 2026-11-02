using System.IO;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;

namespace Hitbench.Services;

public static partial class OHsExportAudio
{
    internal static bool CanMixWithoutClipping(IEnumerable<OHsExportAsset> assets, Func<string, DecodedAudio> resolve, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(asset.Id)) continue;
            var sources = asset.Sources.Select(source =>
            {
                token.ThrowIfCancellationRequested();
                if (source.GainPercent < 0) throw new InvalidDataException("Sample PCM/gain is invalid.");
                var audio = resolve(source.FileName);
                token.ThrowIfCancellationRequested();
                if (audio.Samples.Length == 0) throw new InvalidDataException("Invalid export PCM.");
                return (Audio: audio, Count: audio.Samples.Length, Gain: source.GainPercent / 100f, ExactGain: source.GainPercent / 100d);
            }).ToArray();
            var count = sources.Select(s => s.Count).DefaultIfEmpty().Max();
            for (var i = 0; i < count; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                float rendered = 0;
                double reference = 0;
                foreach (var source in sources)
                {
                    if (i >= source.Count) continue;
                    var value = source.Audio.Samples[i];
                    if (!float.IsFinite(value)) throw new InvalidDataException("Sample PCM/gain is invalid.");
                    rendered += value * source.Gain;
                    reference += value * source.ExactGain;
                }
                if (!float.IsFinite(rendered) || !double.IsFinite(reference)) throw new InvalidDataException("Sample PCM/gain is invalid.");
                if (Math.Abs(rendered) > 1 || Math.Abs(reference) > 1) return false;
            }
        }
        token.ThrowIfCancellationRequested();
        return true;
    }

    internal static bool IsPlaybackEquivalent(OHsExportInput input, OHsExportPlan plan, Func<string, DecodedAudio> resolve, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var volumeAt = OHsExportPlanner.CreateTimingVolumeResolver(input);
        var reference = new List<(long Start, long End, DecodedAudio Audio, double Gain)>();
        int? groupTime = null;
        foreach (var voice in input.Voices.OrderBy(v => v.Timestamp))
        {
            token.ThrowIfCancellationRequested();
            if (groupTime is null || (long)voice.Timestamp - groupTime.Value > 1) groupTime = voice.Timestamp;
            var audio = resolve(voice.FileName);
            var start = ControlPointVolumeTimeline.ToFrame(groupTime.Value);
            reference.Add((start, checked(start + audio.FrameCount), audio, voice.GainPercent / 100d * volumeAt((decimal)groupTime.Value + 5) / 100d));
        }
        var exported = new List<(long Start, long End, (string FileName, int GainPercent)[] Sources, DecodedAudio[] Audio, int Volume)>();
        foreach (var circle in plan.Circles)
        {
            token.ThrowIfCancellationRequested();
            var lookup = (decimal)circle.Timestamp + 5;
            var state = plan.TimingPoints.LastOrDefault(p => p.Timestamp <= lookup) ?? plan.TimingPoints.FirstOrDefault();
            var volume = state?.Volume ?? 100;
            foreach (var slot in plan.Banks[circle.SampleIndex])
            {
                var normal = slot.Key.Type == SampleType.Normal;
                var flag = slot.Key.Type switch { SampleType.Whistle => 2, SampleType.Finish => 4, SampleType.Clap => 8, _ => 1 };
                if (normal ? slot.Key.Sampleset != circle.NormalSet : slot.Key.Sampleset != circle.AdditionSet || (circle.HitSound & flag) == 0) continue;
                var sources = slot.Value.Sources.ToArray();
                if (sources.Length == 0) continue;
                var audio = sources.Select(s => resolve(s.FileName)).ToArray();
                var start = ControlPointVolumeTimeline.ToFrame(circle.Timestamp);
                exported.Add((start, checked(start + audio.Max(a => a.FrameCount)), sources, audio, volume));
            }
        }
        var boundaries = reference.SelectMany(v => new[] { v.Start, v.End }).Concat(exported.SelectMany(v => new[] { v.Start, v.End })).Distinct().Order().ToArray();
        for (var b = 0; b + 1 < boundaries.Length; b++)
        {
            token.ThrowIfCancellationRequested();
            var originals = reference.Where(v => v.Start <= boundaries[b] && v.End > boundaries[b]).ToArray();
            var outputs = exported.Where(v => v.Start <= boundaries[b] && v.End > boundaries[b]).ToArray();
            if (originals.Length == 0 && outputs.Length == 0) continue;
            for (var frame = boundaries[b]; frame < boundaries[b + 1]; frame++)
            {
                if ((frame & 4095) == 0) token.ThrowIfCancellationRequested();
                for (var channel = 0; channel < 2; channel++)
                {
                    double expected = 0, actual = 0;
                    foreach (var voice in originals) expected += voice.Audio.Samples[checked((int)((frame - voice.Start) * 2 + channel))] * voice.Gain;
                    foreach (var voice in outputs)
                    {
                        double sample = 0;
                        var local = frame - voice.Start;
                        for (var j = 0; j < voice.Sources.Length; j++)
                            if (local < voice.Audio[j].FrameCount)
                                sample += voice.Audio[j].Samples[checked((int)(local * 2 + channel))] * voice.Sources[j].GainPercent / 100d;
                        actual += Math.Clamp(sample, -1, 1) * voice.Volume / 100d;
                    }
                    if (Math.Abs(Math.Clamp(expected, -1, 1) - Math.Clamp(actual, -1, 1)) > 1e-6) return false;
                }
            }
        }
        return true;
    }
}
