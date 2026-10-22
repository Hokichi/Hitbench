using Soundie.Resources.DataModels;
namespace Soundie.Resources.Messages;

public sealed record InstrumentalSaveStateChangedMessage(InstrumentalTrackContext Context, long Version, bool IsSaving, string? Error);
