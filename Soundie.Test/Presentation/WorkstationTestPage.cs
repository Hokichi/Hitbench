using Soundie.ViewModels.Shell;
using Soundie.Views.Pages;

namespace Soundie.Test.Presentation;

public static class WorkstationTestPage
{
    public static WorkstationPage Create() => new(new WorkstationPageVM(
        new StubWorkstationSourceService(), new Soundie.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler()));
}
