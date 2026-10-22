using Soundie.Resources.DataModels;
namespace Soundie.Resources.Messages;

public sealed record PlaceInstrumentalNoteRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, string Key, double RequestedMilliseconds, int Subdivision);
