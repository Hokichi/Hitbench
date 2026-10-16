using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class WorkstationVolumeInputLockTests
{
    [SetUp]
    public void Setup() => new VolumeWheelTests().SetUp();

    [TestCase(Key.Space, ModifierKeys.None)]
    [TestCase(Key.Space, ModifierKeys.Shift)]
    [TestCase(Key.Z, ModifierKeys.Control)]
    [TestCase(Key.Y, ModifierKeys.Control)]
    [TestCase(Key.Home, ModifierKeys.Control)]
    [TestCase(Key.End, ModifierKeys.Control)]
    [TestCase(Key.Right, ModifierKeys.None)]
    [TestCase(Key.D3, ModifierKeys.Control)]
    [TestCase(Key.NumPad8, ModifierKeys.Control)]
    public void BackgroundShortcutsAreConsumedWithoutChangingState(Key key, ModifierKeys modifiers)
    {
        var (page, vm, view) = CreatePage();
        using (view)
        {
            vm.PlaceSample(vm.Tracks.Single(), 125);
            UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
            var position = vm.Position;
            var subdivision = vm.Subdivision;
            page.ShowVolumeOverlay(false, new Point(), false, false);
            foreach (var fading in new[] { false, true })
            {
                if (fading) page.ProcessVolumeOverlayIdle(TimeSpan.FromSeconds(1), true);
                Assert.That(page.HandlePlaybackKey(key, modifiers, false, page), Is.True);
                UiTestPump.Run(page.PlaybackInputCompletion);
                Assert.That(vm.IsPlaying, Is.True);
                Assert.That(vm.Position, Is.EqualTo(position));
                Assert.That(vm.Subdivision, Is.EqualTo(subdivision));
                Assert.That(vm.CanUndo, Is.True);
                Assert.That(vm.CanRedo, Is.False);
                Assert.That(vm.Tracks.Single().Samples, Has.Count.EqualTo(1));
            }
        }
    }

    [TestCase(ModifierKeys.Control)]
    [TestCase(ModifierKeys.Shift)]
    [TestCase(ModifierKeys.Control | ModifierKeys.Shift)]
    public void ModifiedWheelCannotNavigateBackgroundWhileOverlayOpen(ModifierKeys modifiers)
    {
        var (page, vm, view) = CreatePage();
        using (view)
        {
            var zoom = vm.ZoomFactor;
            var height = vm.TrackHeight;
            var subdivision = vm.SelectedSubdivisionIndex;
            page.ShowVolumeOverlay(false, new Point(), false, false);
            vm.ScrollWithMarker = true;
            Assert.That(page.HandleContextWheel(modifiers, 120, page), Is.True);
            Assert.That(vm.ScrollWithMarker, Is.True);
            vm.ScrollWithMarker = true;
            Assert.That(page.HandleWaveformWheel(modifiers, 120), Is.True);
            Assert.That(vm.ScrollWithMarker, Is.True);
            Assert.That(vm.ZoomFactor, Is.EqualTo(zoom));
            Assert.That(vm.TrackHeight, Is.EqualTo(height));
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(subdivision));
        }
    }

    [Test]
    public void VolumeKeyboardAndScrollRemainAvailable()
    {
        var (page, vm, view) = CreatePage();
        using (view)
        {
            var subdivision = vm.SelectedSubdivisionIndex;
            page.ShowVolumeOverlay(false, new Point(), false, false);
            Assert.That(page.HandleContextWheel(ModifierKeys.None, -120, page.VolumeOverlay), Is.False);
            Assert.That(page.HandleContextWheel(ModifierKeys.None, -120, page), Is.True);
            Assert.That(page.HandleContextWheel(ModifierKeys.Alt, -120, page.AudioCard), Is.True);
            Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(subdivision));
            Assert.That(vm.ApplicationVolume, Is.EqualTo(95));
            Assert.That(page.HandleVolumeOverlayKey(Key.Down, ModifierKeys.None), Is.True);
            Assert.That(vm.ApplicationVolume, Is.EqualTo(90));
            var active = page.ActiveVolumeWheel;
            Assert.That(page.HandleVolumeOverlayKey(Key.Tab, ModifierKeys.None), Is.True);
            Assert.That(page.ActiveVolumeWheel, Is.SameAs(active));
            Assert.That(page.HandleVolumeOverlayKey(Key.Tab, ModifierKeys.Shift), Is.True);
            Assert.That(page.ActiveVolumeWheel, Is.SameAs(active));
            Assert.That(page.HandleVolumeOverlayKey(Key.Escape, ModifierKeys.None), Is.True);
            Assert.That(page.IsVolumeOverlayOpen, Is.False);
            Assert.That(((Grid)page.FindName("WorkstationContent")).IsEnabled, Is.True);
            Assert.That(page.HandlePlaybackKey(Key.Space, ModifierKeys.None, false, page), Is.True);
            UiTestPump.Run(page.PlaybackInputCompletion);
            Assert.That(vm.IsPlaying, Is.True);
        }
    }

    private static (WorkstationPage, WorkstationPageVM, InMemoryView) CreatePage()
    {
        var track = new Track { SelectedSampleFileName = "hit.wav" };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map.osu", "audio.wav", new ProjectData { Offsets = [0], BPMs = [120], Tracks = [track] })) };
        var vm = new WorkstationPageVM(source, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), new StubSampleFileService(["hit.wav"]));
        var page = new WorkstationPage(vm);
        page.SetProjectPath("project.json");
        var view = new InMemoryView(new Window { Content = page, Width = 1000, Height = 700 });
        UiTestPump.Run(page.LoadCompletion);
        return (page, vm, view);
    }
}

