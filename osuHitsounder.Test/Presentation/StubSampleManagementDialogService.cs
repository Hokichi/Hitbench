using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.Dialogs;

namespace osuHitsounder.Test.Presentation;

public sealed class StubSampleManagementDialogService : ISampleManagementDialogService
{
    public Func<SampleManagementContext, CancellationToken, Task> Present { get; set; } = (_, _) => Task.CompletedTask;
    public Task ShowAsync(SampleManagementContext context, CancellationToken token) => Present(context, token);
}
