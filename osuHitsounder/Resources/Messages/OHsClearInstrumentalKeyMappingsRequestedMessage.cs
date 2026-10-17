using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.Messages;
public sealed record OHsClearInstrumentalKeyMappingsRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, long ExpectedRevision);
