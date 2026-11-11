using System.Numerics;

namespace Hitbench.Resources.DataModels;

public sealed record OHsMidiAttack(int NoteNumber, long Tick, BigInteger ElapsedMicrosecondsNumerator);
