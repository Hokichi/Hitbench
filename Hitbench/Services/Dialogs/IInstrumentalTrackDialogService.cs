using Hitbench.Resources.DataModels;
namespace Hitbench.Services.Dialogs;

public interface IInstrumentalTrackDialogService
{
    Task ShowArrangementAsync(InstrumentalEditorContext context, CancellationToken token);
    Task ShowKeysMappingAsync(InstrumentalTrackSnapshot snapshot, CancellationToken token);
}
