using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;
public sealed record ReplayInstrumentalNoteHistoryRequestedMessage(InstrumentalTrackContext Context, Guid RequestId, bool Redo);
