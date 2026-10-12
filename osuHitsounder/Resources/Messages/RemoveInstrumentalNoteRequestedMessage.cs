using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.Messages;
public sealed record RemoveInstrumentalNoteRequestedMessage(InstrumentalTrackContext Context,Guid RequestId,Guid NoteId);
