using Soundie.Resources.DataModels;
namespace Soundie.Services.Dialogs;

public interface IInstrumentalTrackDialogService
{
    Task ShowArrangementAsync(InstrumentalEditorContext context, CancellationToken token);
    Task ShowKeysMappingAsync(InstrumentalTrackSnapshot snapshot, CancellationToken token);
}
