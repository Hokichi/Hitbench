using Soundie.Resources.DataModels;
namespace Soundie.Resources.Messages;
public sealed record ReplayInstrumentalNoteHistoryRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, bool Redo);
