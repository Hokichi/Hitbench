using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.Messages;

public sealed record MoveInstrumentalNoteRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, Guid NoteId, string Key, double RequestedMilliseconds, int Subdivision);
