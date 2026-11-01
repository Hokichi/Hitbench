using System.Numerics;

namespace Hitbench.Resources.DataModels;

public sealed record OHsExportEnvelopeSegment
{
    public long StartFrame { get; }
    public long EndFrame { get; }
    public BigInteger StartNumerator { get; }
    public BigInteger SlopeNumerator { get; }
    public BigInteger Denominator { get; }
    private readonly double startGain;
    private readonly double slope;
    public OHsExportEnvelopeSegment(long startFrame, long endFrame, BigInteger startNumerator, BigInteger slopeNumerator, BigInteger denominator)
    {
        if (startFrame < 0 || endFrame <= startFrame || denominator <= 0) throw new ArgumentOutOfRangeException(nameof(endFrame));
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(startNumerator, slopeNumerator), denominator);
        StartFrame = startFrame; EndFrame = endFrame;
        StartNumerator = startNumerator / gcd; SlopeNumerator = slopeNumerator / gcd; Denominator = denominator / gcd;
        startGain = (double)StartNumerator / (double)Denominator; slope = (double)SlopeNumerator / (double)Denominator;
        if (!double.IsFinite(startGain) || !double.IsFinite(slope) || startGain < 0 || GetGain(endFrame - 1) < 0)
            throw new ArgumentException("Invalid export envelope.");
    }
    public double GetGain(long frame) => startGain + slope * (frame - StartFrame);
    internal string Key => FormattableString.Invariant($"{StartFrame}:{EndFrame}:{StartNumerator}:{SlopeNumerator}:{Denominator}");
}
