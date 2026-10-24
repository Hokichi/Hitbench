using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;

public sealed record RemoveInstrumentalNoteRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, Guid NoteId);
