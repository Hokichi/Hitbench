using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.Messages;
public sealed record ReplayInstrumentalNoteHistoryRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, bool Redo);
