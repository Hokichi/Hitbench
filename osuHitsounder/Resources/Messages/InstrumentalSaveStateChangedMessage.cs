using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.Messages;
public sealed record InstrumentalSaveStateChangedMessage(InstrumentalTrackContext Context,long Version,bool IsSaving,string? Error);
