using Onset.Resources.DataModels;
namespace Onset.Resources.Messages;

public sealed record InstrumentalSaveStateChangedMessage(InstrumentalTrackContext Context, long Version, bool IsSaving, string? Error);
