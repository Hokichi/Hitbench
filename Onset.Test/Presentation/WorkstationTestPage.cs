using Onset.ViewModels.Shell;
using Onset.Views.Pages;

namespace Onset.Test.Presentation;

public static class WorkstationTestPage
{
    public static WorkstationPage Create() => new(new WorkstationPageVM(
        new StubWorkstationSourceService(), new Onset.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler()));
}
