using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationProjectRefreshIntegrationTests
{
    [SetUp] public void Setup() => new VolumeWheelTests().SetUp();

    [Test]
    public void FailedRefreshKeepsToolbarRetryAvailable()
    {
        var calls = 0;
        var source = new StubWorkstationSourceService { Load = (_, _) => ++calls == 2
            ? Task.FromException<WorkstationSource>(new IOException("reload failed"))
            : Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Offsets = [0], BPMs = [120] })) };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        UiTestPump.Run(vm.RefreshProjectCommand.ExecuteAsync(null)); view.UpdateLayout();
        Assert.That(vm.LoadPhase, Is.EqualTo(ProjectLoadPhase.Failed));
        Assert.That(page.FindName("RefreshProjectButton"), Is.TypeOf<BalloonButton>());
        Assert.That(((BalloonButton)page.FindName("RefreshProjectButton")).IsEnabled, Is.True);
        Assert.That(((ScrollViewer)page.FindName("WaveformScrollViewer")).IsEnabled, Is.False);
        Assert.That(page.HandlePlaybackKey(Key.F5, ModifierKeys.None, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion); view.UpdateLayout(); view.UpdateLayout();
        Assert.That(vm.CanPlay, Is.True); UiTestPump.Run(page.DeactivateAsync());
    }

    [Test]
    public void RefreshToolbarUsesExistingContractAndFreshPresentationFrames()
    {
        var saves = new DelayedProjectFileService(); saves.Release(); var calls = 0;
        var source = new StubWorkstationSourceService { Load = (_, _) =>
        {
            calls++;
            return Task.FromResult(new WorkstationSource("map", "song", new ProjectData
            { Tracks = [new Track { Name = calls == 1 ? "initial" : "reloaded", SelectedSampleFileName = "hit.wav" }], Offsets = [0], BPMs = [120] }));
        } };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), new StubSampleFileService(["hit.wav"]), projectFiles: saves);
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 }, processFrames: false);
        UiTestPump.Run(StubWorkstationAudioService.WaitUntilAsync(() => vm.LoadPhase == ProjectLoadPhase.AwaitingPresentation));
        view.UpdateLayout(); page.ProcessPresentationFrame(TimeSpan.FromMilliseconds(1)); page.ProcessPresentationFrame(TimeSpan.FromMilliseconds(2));
        UiTestPump.Run(page.LoadCompletion);
        var action = page.FindName("RefreshProjectButton");
        Assert.That(action, Is.TypeOf<BalloonButton>());
        var button = (BalloonButton)action;
        Assert.That(button.IsTabStop, Is.True);
        UiControlContract.AssertStyle<BalloonButton>(button, "OHsWorkstationRefreshButtonStyle");
        Assert.That(button.Style.BasedOn, Is.SameAs(button.FindResource("OHsWorkstationToolbarButtonStyle")));
        Assert.That(button.FocusVisualStyle, Is.SameAs(button.FindResource("OHsSampleManagerKeyboardFocusStyle")));
        Assert.That(button.Command, Is.SameAs(vm.RefreshProjectCommand)); Assert.That(button.IsEnabled, Is.True);
        Assert.That(AutomationProperties.GetName(button), Is.EqualTo("Refresh Project"));
        var name = Descendants(page).OfType<TextBox>().First(input => input.DataContext is Track);
        UiTestPump.Run(StubWorkstationAudioService.WaitUntilAsync(() => name.IsLoaded));
        name.Text = "name edited before refresh";
        Assert.That(vm.Tracks.Single().Name, Is.EqualTo("name edited before refresh"));
        Assert.That(page.HandlePlaybackKey(Key.F5, ModifierKeys.None, false, name), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(saves.LatestJson, Does.Contain("name edited before refresh"));
        Assert.That(vm.Tracks.Single().Name, Is.EqualTo("reloaded"));
        Assert.That(vm.CanPlay, Is.False); Assert.That(button.IsEnabled, Is.False);
        view.UpdateLayout(); page.ProcessPresentationFrame(TimeSpan.FromMilliseconds(3));
        Assert.That(vm.CanPlay, Is.False);
        page.ProcessPresentationFrame(TimeSpan.FromMilliseconds(4));
        Assert.That(vm.CanPlay, Is.True); Assert.That(button.IsEnabled, Is.True);
        Assert.That(page.HandlePlaybackKey(Key.F5, ModifierKeys.None, true, page), Is.True);
        Assert.That(calls, Is.EqualTo(2));
        UiTestPump.Run(page.DeactivateAsync());
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
