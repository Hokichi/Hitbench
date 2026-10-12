using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.Messages;
public sealed record InstrumentalEditResultMessage(InstrumentalTrackContext Context,Guid RequestId,bool Changed,Guid? SelectedNoteId,string? Error);
