using Hitbench.Resources.DataModels;
using Hitbench.Services.Dialogs;

namespace Hitbench.Test.Presentation;

public sealed class StubSampleManagementDialogService : ISampleManagementDialogService
{
    public Func<SampleManagementContext, CancellationToken, Task> PrepareSkin { get; set; } = (_, _) => Task.CompletedTask;
    public Task PrepareSkinDefaultsRefreshAsync(SampleManagementContext context, CancellationToken token) => PrepareSkin(context, token);
    public Func<SampleManagementContext, CancellationToken, Task> Present { get; set; } = (_, _) => Task.CompletedTask;
    public Task ShowAsync(SampleManagementContext context, CancellationToken token) => Present(context, token);
    public Func<SampleManagementContext, CancellationToken, Task<bool>> Close { get; set; } = (_, _) => Task.FromResult(false);
    public Task<bool> TryCloseAsync(SampleManagementContext context, CancellationToken token) => Close(context, token);
}
