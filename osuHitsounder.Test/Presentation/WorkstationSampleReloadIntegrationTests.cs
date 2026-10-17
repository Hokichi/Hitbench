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
public class WorkstationSampleReloadIntegrationTests
{
    [SetUp] public void Setup() => new VolumeWheelTests().SetUp();

    [Test]
    public void FailedSampleReloadKeepsToolbarRetryAvailable()
    {
        var source = new StubWorkstationSourceService();
        var files = new StubSampleFileService(["hit.wav"]);
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), files);
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        UiTestPump.Run(page.LoadCompletion);
        files.List = (_, _) => Task.FromException<IReadOnlyList<string>>(new IOException("listing failed"));
        UiTestPump.Run(vm.ReloadSamplesCommand.ExecuteAsync(null)); view.UpdateLayout();
        Assert.That(vm.LoadPhase, Is.EqualTo(ProjectLoadPhase.Ready));
        Assert.That(((osuHitsounder.Views.Pages.OHsWorkstationAudioCard)page.FindName("AudioCard")).FindName("ReloadSamplesButton"), Is.TypeOf<BalloonButton>());
        Assert.That(((BalloonButton)((osuHitsounder.Views.Pages.OHsWorkstationAudioCard)page.FindName("AudioCard")).FindName("ReloadSamplesButton")).IsEnabled, Is.True);
        Assert.That(((ScrollViewer)(page.Timeline).FindName("WaveformScrollViewer")).IsEnabled, Is.True);
        files.List = (_, _) => Task.FromResult<IReadOnlyList<string>>(["new.wav"]);
        Assert.That(page.HandlePlaybackKey(Key.F5, ModifierKeys.None, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion); view.UpdateLayout();
        Assert.That(vm.SampleFiles, Is.EqualTo(new[] { "new.wav" }));
        Assert.That(vm.ErrorMessage, Is.Null); UiTestPump.Run(page.DeactivateAsync());
    }

    [Test]
    public void ReloadSamplesToolbarKeepsContractAndReadyPresentation()
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
        var action = ((osuHitsounder.Views.Pages.OHsWorkstationAudioCard)page.FindName("AudioCard")).FindName("ReloadSamplesButton");
        Assert.That(action, Is.TypeOf<BalloonButton>());
        var button = (BalloonButton)action;
        Assert.That(button.IsTabStop, Is.True);
        UiControlContract.AssertStyle<BalloonButton>(button, "OHsWorkstationToolbarButtonStyle");
        Assert.That(button.Style, Is.SameAs(button.FindResource("OHsWorkstationToolbarButtonStyle")));
        Assert.That(button.FocusVisualStyle, Is.SameAs(button.FindResource("OHsSampleManagerKeyboardFocusStyle")));
        Assert.That(button.Command, Is.SameAs(vm.ReloadSamplesCommand)); Assert.That(button.IsEnabled, Is.True);
        Assert.That(AutomationProperties.GetName(button), Is.EqualTo("Refresh samples"));
        var name = Descendants(page).OfType<TextBox>().First(input => input.DataContext is Track);
        UiTestPump.Run(StubWorkstationAudioService.WaitUntilAsync(() => name.IsLoaded));
        vm.BeginTrackRenameCommand.Execute(vm.Tracks.Single());
        name.SetCurrentValue(TextBox.TextProperty, "name edited before refresh");
        UiTestPump.Run(vm.CommitTrackRenameAsync(vm.Tracks.Single()));
        Assert.That(vm.Tracks.Single().Name, Is.EqualTo("name edited before refresh"));
        Assert.That(page.HandlePlaybackKey(Key.F5, ModifierKeys.None, false, name), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(saves.LatestJson, Does.Contain("name edited before refresh"));
        Assert.That(vm.Tracks.Single().Name, Is.EqualTo("name edited before refresh"));
        Assert.That(vm.CanPlay, Is.True); Assert.That(button.IsEnabled, Is.True);
        Assert.That(button.ButtonText, Is.EqualTo("Refresh samples"));
        Assert.That(button.ToolTip, Is.EqualTo("Refresh project samples (F5)"));
        Assert.That(page.HandlePlaybackKey(Key.F5, ModifierKeys.None, true, page), Is.True);
        Assert.That(calls, Is.EqualTo(1));
        UiTestPump.Run(page.DeactivateAsync());
    }

    [Test]
    public void ReloadUpdatesMissingSampleCuesWithoutMovingTimelineOrTracks()
    {
        var tracks = Enumerable.Range(0, 30).Select(_ => new Track { SelectedSampleFileName = "hit.wav" }).ToList();
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song",
            new ProjectData { Tracks = tracks, Offsets = [0], BPMs = [120] })) };
        var files = new StubSampleFileService(["hit.wav"]);
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), files);
        var page = new WorkstationPage(vm); page.SetProjectPath("project.json");
        using var view = new InMemoryView(new Window { Content = page, Width = 900, Height = 600 });
        try
        {
            UiTestPump.Run(page.LoadCompletion);
            var list = (ListBox)((osuHitsounder.Views.Pages.OHsWorkstationTrackList)page.FindName("TrackListComponent")).FindName("TrackList");
            var scroll = Descendants(list).OfType<ScrollViewer>().First();
            scroll.ScrollToVerticalOffset(200); view.UpdateLayout();
            var vertical = scroll.VerticalOffset; var horizontal = page.TimelineHorizontalOffset;
            files.List = (_, _) => Task.FromResult<IReadOnlyList<string>>([]);
            UiTestPump.Run(vm.ReloadSamplesCommand.ExecuteAsync(null)); view.UpdateLayout();
            Assert.That(scroll.VerticalOffset, Is.EqualTo(vertical)); Assert.That(page.TimelineHorizontalOffset, Is.EqualTo(horizontal));
            Assert.That(Descendants(list).OfType<TextBlock>().Where(t => t.Name == "MissingGridStatus").Select(t => t.Text),
                Is.Not.Empty.And.All.EqualTo("Sample not found."));
            Assert.That(tracks.Select(t => t.SelectedSampleFileName), Is.All.EqualTo("hit.wav"));
            files.List = (_, _) => Task.FromResult<IReadOnlyList<string>>(["hit.wav"]);
            UiTestPump.Run(vm.ReloadSamplesCommand.ExecuteAsync(null)); view.UpdateLayout();
            Assert.That(Descendants(list).OfType<ComboBox>().Where(c => c.Name == "TrackSampleFileSelector").Select(c => c.SelectedItem),
                Is.Not.Empty.And.All.EqualTo("hit.wav"));
            Assert.That(scroll.VerticalOffset, Is.EqualTo(vertical));
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
