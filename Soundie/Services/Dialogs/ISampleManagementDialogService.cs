using Soundie.Resources.DataModels;

namespace Soundie.Services.Dialogs;

public interface ISampleManagementDialogService
{
    Task ShowAsync(SampleManagementContext context, CancellationToken token);
}
