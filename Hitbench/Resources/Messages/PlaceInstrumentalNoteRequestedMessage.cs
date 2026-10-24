using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;

public sealed record PlaceInstrumentalNoteRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, string Key, double RequestedMilliseconds, int Subdivision);
