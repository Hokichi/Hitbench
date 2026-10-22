using Soundie.Resources.DataModels;
namespace Soundie.Resources.Messages;
public sealed record OHsClearInstrumentalKeyMappingsRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, long ExpectedRevision);
