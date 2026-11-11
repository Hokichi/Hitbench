using System.Numerics;

namespace Hitbench.Resources.DataModels;

public sealed record OHsMidiTempoEvent(long Tick, int MicrosecondsPerQuarterNote, BigInteger ElapsedMicrosecondsNumerator);
