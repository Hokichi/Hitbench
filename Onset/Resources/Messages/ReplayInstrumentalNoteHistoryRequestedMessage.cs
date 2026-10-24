using Onset.Resources.DataModels;
namespace Onset.Resources.Messages;
public sealed record ReplayInstrumentalNoteHistoryRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, bool Redo);
