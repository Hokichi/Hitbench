using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Services.Dialogs;

public interface IInstrumentalTrackDialogService
{
    Task ShowArrangementAsync(InstrumentalEditorContext context, CancellationToken token);
    Task ShowKeysMappingAsync(InstrumentalTrackSnapshot snapshot, CancellationToken token);
}
