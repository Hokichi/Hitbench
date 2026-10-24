using Onset.Resources.DataModels;
namespace Onset.Resources.Messages;

public sealed record PlaceInstrumentalNoteRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, string Key, double RequestedMilliseconds, int Subdivision);
