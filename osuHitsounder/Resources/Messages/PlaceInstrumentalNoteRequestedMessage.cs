using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.Messages;
public sealed record PlaceInstrumentalNoteRequestedMessage(InstrumentalTrackContext Context,Guid RequestId,string Key,double RequestedMilliseconds,int Subdivision);
