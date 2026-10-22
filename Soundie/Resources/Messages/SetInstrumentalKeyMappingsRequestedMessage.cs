using System.Collections.ObjectModel;
using Soundie.Resources.DataModels;

namespace Soundie.Resources.Messages;

public sealed class SetInstrumentalKeyMappingsRequestedMessage(InstrumentalTrackContext context, Guid requestId, string folderName, IReadOnlyDictionary<string, string> mappings)
{
    public InstrumentalTrackContext Context { get; } = context;
    public Guid RequestId { get; } = requestId;
    public string FolderName { get; } = folderName;
    public IReadOnlyDictionary<string, string> Mappings { get; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(mappings));
}
