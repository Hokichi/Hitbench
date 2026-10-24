using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;

public sealed record MoveInstrumentalNoteRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, Guid NoteId, string Key, double RequestedMilliseconds, int Subdivision);
