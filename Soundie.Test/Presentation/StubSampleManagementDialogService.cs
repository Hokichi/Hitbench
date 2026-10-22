using Soundie.Resources.DataModels;
using Soundie.Services.Dialogs;

namespace Soundie.Test.Presentation;

public sealed class StubSampleManagementDialogService : ISampleManagementDialogService
{
    public Func<SampleManagementContext, CancellationToken, Task> Present { get; set; } = (_, _) => Task.CompletedTask;
    public Task ShowAsync(SampleManagementContext context, CancellationToken token) => Present(context, token);
}
