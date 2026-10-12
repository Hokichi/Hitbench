using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.Messages;
public sealed record SetInstrumentalKeyMappingRequestedMessage(InstrumentalTrackContext Context,Guid RequestId,string Key,string? FileName);
