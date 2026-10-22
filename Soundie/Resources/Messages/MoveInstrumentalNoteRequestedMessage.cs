using Soundie.Resources.DataModels;
namespace Soundie.Resources.Messages;

public sealed record MoveInstrumentalNoteRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, Guid NoteId, string Key, double RequestedMilliseconds, int Subdivision);
