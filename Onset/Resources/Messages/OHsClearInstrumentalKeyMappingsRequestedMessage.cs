using Onset.Resources.DataModels;
namespace Onset.Resources.Messages;
public sealed record OHsClearInstrumentalKeyMappingsRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, long ExpectedRevision);
