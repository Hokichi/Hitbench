namespace Onset.Core.Entities;

public sealed record WaveformData(TimeSpan Duration, IReadOnlyList<float> Peaks);
