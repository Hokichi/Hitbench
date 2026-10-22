using Soundie.Resources.DataModels;
namespace Soundie.Services.Dialogs;
public interface IOHsExportDialogService
{
    Task ShowAsync(OHsExportRequest request, CancellationToken token);
}
