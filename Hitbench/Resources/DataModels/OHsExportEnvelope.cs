using System.Numerics;
using Hitbench.Services;

namespace Hitbench.Resources.DataModels;

public sealed class OHsExportEnvelope
{
    public IReadOnlyList<OHsExportEnvelopeSegment> Segments { get; }
    public bool IsConstant => Segments.Count == 1 && Segments[0].SlopeNumerator.IsZero;
    public bool IsUnity => IsConstant && Segments[0].StartNumerator == Segments[0].Denominator;
    public string Key => IsConstant ? FormattableString.Invariant($"C:{Segments[0].StartNumerator}/{Segments[0].Denominator}")
        : string.Join('|', Segments.Select(s => s.Key));
    private OHsExportEnvelope(IEnumerable<OHsExportEnvelopeSegment> segments)
    {
        var normalized = new List<OHsExportEnvelopeSegment>();
        foreach (var next in segments)
        {
            if (normalized.LastOrDefault() is { } previous && previous.EndFrame == next.StartFrame &&
                previous.SlopeNumerator * next.Denominator == next.SlopeNumerator * previous.Denominator &&
                (previous.StartNumerator + previous.SlopeNumerator * (next.StartFrame - previous.StartFrame)) * next.Denominator == next.StartNumerator * previous.Denominator)
                normalized[^1] = new(previous.StartFrame, next.EndFrame, previous.StartNumerator, previous.SlopeNumerator, previous.Denominator);
            else normalized.Add(next);
        }
        Segments = Array.AsReadOnly(normalized.ToArray());
    }
    public static OHsExportEnvelope Create(ControlPointVolumeTimeline timeline, long startFrame, long frameCount)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (startFrame < 0 || frameCount <= 0) throw new ArgumentOutOfRangeException(nameof(frameCount));
        var end = checked(startFrame + frameCount);
        var segments = new List<OHsExportEnvelopeSegment>();
        for (var frame = startFrame; frame < end;)
        {
            var index = timeline.FindSegment(frame);
            var source = index < 0 ? null : timeline.Segments[index];
            var finish = Math.Min(end, timeline.GetNextBoundary(frame));
            if (source is not { IsRamp: true } || source.EndVolume == source.StartVolume)
                segments.Add(new(frame - startFrame, finish - startFrame, source?.StartVolume ?? 100, 0, 100));
            else
            {
                BigInteger duration = source.EndFrame - source.StartFrame;
                BigInteger delta = source.EndVolume - source.StartVolume;
                segments.Add(new(frame - startFrame, finish - startFrame,
                    source.StartVolume * duration + delta * (frame - source.StartFrame), delta, 100 * duration));
            }
            frame = finish;
        }
        return new(segments);
    }
    public OHsExportEnvelope Scale(int numerator, int denominator)
    {
        if (numerator < 0 || denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        return new(Segments.Select(s => new OHsExportEnvelopeSegment(s.StartFrame, s.EndFrame,
            s.StartNumerator * numerator, s.SlopeNumerator * numerator, s.Denominator * denominator)));
    }
    public double GetGain(long relativeFrame)
    {
        var low = 0; var high = Segments.Count;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (Segments[mid].StartFrame <= relativeFrame) low = mid + 1; else high = mid;
        }
        if (relativeFrame < 0 || low == 0 || relativeFrame >= Segments[^1].EndFrame) throw new ArgumentOutOfRangeException(nameof(relativeFrame));
        return Segments[low - 1].GetGain(relativeFrame);
    }
}
