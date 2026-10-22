using Soundie.Resources.DataModels;
namespace Soundie.Resources.Messages;

public sealed record SetInstrumentalKeyMappingRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, string Key, string? FileName, string? FolderName = null);
