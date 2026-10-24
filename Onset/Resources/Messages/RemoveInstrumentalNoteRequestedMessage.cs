using Onset.Resources.DataModels;
namespace Onset.Resources.Messages;

public sealed record RemoveInstrumentalNoteRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, Guid NoteId);
