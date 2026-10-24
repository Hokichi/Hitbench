using Onset.Resources.DataModels;
namespace Onset.Resources.Messages;

public sealed record MoveInstrumentalNoteRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, Guid NoteId, string Key, double RequestedMilliseconds, int Subdivision);
