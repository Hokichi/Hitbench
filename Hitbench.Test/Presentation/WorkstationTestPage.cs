using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

public static class WorkstationTestPage
{
    public static WorkstationPage Create() => new(new WorkstationPageVM(
        new StubWorkstationSourceService(), new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler()));
}
