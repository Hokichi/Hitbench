using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;
public sealed record OHsClearInstrumentalKeyMappingsRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, long ExpectedRevision);
