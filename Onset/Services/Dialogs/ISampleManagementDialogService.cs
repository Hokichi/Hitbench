using Onset.Resources.DataModels;

namespace Onset.Services.Dialogs;

public interface ISampleManagementDialogService
{
    Task ShowAsync(SampleManagementContext context, CancellationToken token);
}
