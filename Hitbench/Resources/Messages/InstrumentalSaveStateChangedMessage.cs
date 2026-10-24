using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;

public sealed record InstrumentalSaveStateChangedMessage(InstrumentalTrackContext Context, long Version, bool IsSaving, string? Error);
