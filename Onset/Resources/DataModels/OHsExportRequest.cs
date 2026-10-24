namespace Onset.Resources.DataModels;
public sealed record OHsExportRequest(Guid OperationId, string ProjectPath, string BeatmapPath, string AudioPath, string ProjectJson, OHsExportOptions Options);
