namespace Onset.Resources.Messages;
public sealed record OHsExportResultMessage(Guid OperationId, string ProjectPath, bool IsPublished, string? Error);
