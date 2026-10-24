using Onset.Resources.DataModels;
using Onset.Services.Dialogs;

namespace Onset.Test.Presentation;

public sealed class StubSampleManagementDialogService : ISampleManagementDialogService
{
    public Func<SampleManagementContext, CancellationToken, Task> Present { get; set; } = (_, _) => Task.CompletedTask;
    public Task ShowAsync(SampleManagementContext context, CancellationToken token) => Present(context, token);
}
