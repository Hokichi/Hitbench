using Hitbench.Resources.DataModels;

namespace Hitbench.Services.Dialogs;

public interface ISampleManagementDialogService
{
    Task PrepareSkinDefaultsRefreshAsync(SampleManagementContext context, CancellationToken token) => Task.CompletedTask;
    Task ShowAsync(SampleManagementContext context, CancellationToken token);
    Task<bool> TryCloseAsync(SampleManagementContext context, CancellationToken token);
}
