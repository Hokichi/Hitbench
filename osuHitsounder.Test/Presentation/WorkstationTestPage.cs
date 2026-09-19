using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

public static class WorkstationTestPage
{
    public static WorkstationPage Create() => new(new WorkstationPageVM(
        new StubWorkstationSourceService(), new StubWorkstationAudioService(), new ImmediateUiScheduler()));
}
