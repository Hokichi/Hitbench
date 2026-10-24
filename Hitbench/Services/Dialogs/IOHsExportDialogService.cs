using Hitbench.Resources.DataModels;
namespace Hitbench.Services.Dialogs;
public interface IOHsExportDialogService
{
    Task ShowAsync(OHsExportRequest request, CancellationToken token);
}
