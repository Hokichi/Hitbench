using Soundie.Resources.DataModels;
namespace Soundie.Resources.Messages;

public sealed record RemoveInstrumentalNoteRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, Guid NoteId);
