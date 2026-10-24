using Onset.Resources.DataModels;
namespace Onset.Services.Dialogs;

public interface IInstrumentalTrackDialogService
{
    Task ShowArrangementAsync(InstrumentalEditorContext context, CancellationToken token);
    Task ShowKeysMappingAsync(InstrumentalTrackSnapshot snapshot, CancellationToken token);
}
