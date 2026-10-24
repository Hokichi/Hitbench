using Onset.Resources.DataModels;
namespace Onset.Services.Dialogs;
public interface IOHsExportDialogService
{
    Task ShowAsync(OHsExportRequest request, CancellationToken token);
}
