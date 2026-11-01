namespace Hitbench.Resources.DataModels;

public sealed record OHsExportSource(string FileName, int GainPercent, OHsExportEnvelope? Envelope = null);
