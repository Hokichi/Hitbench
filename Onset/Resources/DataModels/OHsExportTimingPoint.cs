namespace Onset.Resources.DataModels;
public sealed record OHsExportTimingPoint(decimal Timestamp, string BeatLength, int Meter, int SampleSet, int SampleIndex, int Volume, int Effects, OHsExportTimingKind Kind);
