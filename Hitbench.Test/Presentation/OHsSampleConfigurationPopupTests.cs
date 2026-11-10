using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Views.Pages;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsSampleConfigurationPopupTests
{
    [Test]
    public void ManagerOpenKeepsSampleSelectionEnabledAndRefreshesAvailableFiles()
    {
        var track = new Track { SelectedSampleFileName = "a.wav", Samples = [new() { FileName = "a.wav", Timestamp = 100 }] };
        var messenger = new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger();
        var entered = new TaskCompletionSource();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            sampleFiles: new StubSampleFileService(["a.wav", "b.wav"]), sampleDialog: new StubSampleFileDialogService([]), messenger: messenger,
            sampleManagementDialog: new StubSampleManagementDialogService { Present = (_, token) =>
                { entered.TrySetResult(); return Task.Delay(Timeout.Infinite, token); } });
        UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
        var row = new OHsWorkstationTrackRow { Track = track, ViewModel = vm, PresentSampleConfiguration = (_, _) => { } };
        using var view = new InMemoryView(new Window { Content = row, Width = 900, Height = 200 });
        try
        {
            vm.ManageSamplesCommand.Execute(null); UiTestPump.Run(entered.Task);
            Assert.That(vm.CanImportSample, Is.False);
            row.OpenSampleConfiguration();
            var selector = (ComboBox)row.FindName("TrackSampleFileSelector");
            Assert.That(selector.IsEnabled, Is.True);
            selector.SelectedItem = selector.Items.Cast<Hitbench.Resources.DataModels.OHsTrackSampleChoice>().Single(c => c.FileName == "b.wav");
            Assert.That(track.SelectedSampleFileName, Is.EqualTo("b.wav"));
            Assert.That(track.Samples[0].FileName, Is.EqualTo("b.wav"));
            messenger.Send(new Hitbench.Resources.Messages.ProjectSamplesChangedMessage(vm.SampleManagementContext!, ["a.wav"], null, null));
            view.UpdateLayout();
            Assert.That(selector.Items.Cast<Hitbench.Resources.DataModels.OHsTrackSampleChoice>().Where(c => !c.IsDefault).Select(c => c.FileName), Is.EqualTo(new[] { "a.wav" }));
            vm.SelectSample(track, "b.wav");
            Assert.That(track.SelectedSampleFileName, Is.EqualTo("b.wav"));
            selector.SelectedItem = selector.Items.Cast<Hitbench.Resources.DataModels.OHsTrackSampleChoice>().Single(c => c.FileName == "a.wav");
            Assert.That(track.SelectedSampleFileName, Is.EqualTo("a.wav"));
            vm.IsReturning = true; view.UpdateLayout();
            Assert.That(selector.IsEnabled, Is.False);
            vm.SelectSample(track, "b.wav");
            Assert.That(track.SelectedSampleFileName, Is.EqualTo("a.wav"));
        }
        finally { row.CloseSampleConfiguration(false); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [SetUp] public void Setup() => new VolumeWheelTests().SetUp();

    private static (WorkstationPageVM Vm, OHsWorkstationTrackRow Row, InMemoryView View) Create(Track track)
    {
        var load = WorkstationSampleConfigurationTests.CreateAsync(track); UiTestPump.Run(load);
        var vm = load.GetAwaiter().GetResult();
        var row = new OHsWorkstationTrackRow { Track = track, ViewModel = vm, PresentSampleConfiguration = (_, _) => { } };
        return (vm, row, new InMemoryView(new Window { Content = row, Width = 900, Height = 200 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DirectActionOffersConfigOnlyForSampleTracks(bool instrumental)
    {
        var (vm, row, view) = Create(instrumental ? new InstrumentalTrack() : new Track());
        using (view) try
        {
            var menu = Descendants(row).OfType<Hitbench.Resources.CustomControls.BalloonComboBox>().Single().ContextMenu;
            Assert.That(menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Config Samples")).Visibility == Visibility.Visible, Is.EqualTo(!instrumental));
            var action = Descendants(row).OfType<Hitbench.Resources.CustomControls.BalloonButton>().Single(button => button.ButtonText == "Config Samples");
            Assert.That(action.Visibility == Visibility.Visible, Is.EqualTo(!instrumental));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SelectorsCommitAllThreeValuesAndRefreshAfterSampleReload(bool collapsed)
    {
        var track = new Track { SelectedSampleFileName = "a.wav", IsCollapsed = collapsed };
        var (vm, row, view) = Create(track);
        using (view) try
        {
            Assert.That(row.OpenSampleConfiguration(), Is.True);
            var file = (ComboBox)row.FindName("TrackSampleFileSelector");
            var addition = (ComboBox)row.FindName("TrackAdditionSelector");
            var type = (ComboBox)row.FindName("TrackSampleTypeSelector");
            foreach (var selector in new[] { file, addition, type })
            {
                UiControlContract.AssertStyle<ComboBox>(selector, "OHsWorkstationTrackMetadataComboBoxStyle");
                Assert.That(selector.IsEnabled, Is.True);
            }
            file.SelectedItem = file.Items.Cast<Hitbench.Resources.DataModels.OHsTrackSampleChoice>().Single(c => c.FileName == "b.wav"); addition.SelectedItem = "Soft"; type.SelectedItem = SampleType.Finish;
            Assert.That(track.SelectedSampleFileName, Is.EqualTo("b.wav"));
            Assert.That(track.Addition, Is.EqualTo("Soft")); Assert.That(track.SampleType, Is.EqualTo(SampleType.Finish));
            vm.SampleFiles.Clear(); vm.SampleFiles.Add("b.wav"); row.RefreshPresentation();
            Assert.That(((Hitbench.Resources.DataModels.OHsTrackSampleChoice)file.SelectedItem).FileName, Is.EqualTo("b.wav"));
            Assert.That(row.HandleSampleConfigurationEscape(), Is.True);
            Assert.That(row.IsSampleConfigurationOpen, Is.False);
        }
        finally { row.CloseSampleConfiguration(false); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [Test]
    public void MenuActivationWaitsForMenuDismissalAndRejectsRecycledOwner()
    {
        var track = new Track(); var (vm, row, view) = Create(track);
        using (view) try
        {
            var menu = Descendants(row).OfType<Hitbench.Resources.CustomControls.BalloonComboBox>().Single().ContextMenu;
            var config = Descendants(row).OfType<Hitbench.Resources.CustomControls.BalloonButton>().Single(button => button.ButtonText == "Config Samples");
            config.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, config));
            Assert.That(row.IsSampleConfigurationOpen, Is.False, "Native menu must finish closing before popup presentation.");
            row.Track = new Track();
            UiTestPump.Run(row.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
            Assert.That(vm.IsSampleConfigurationOpen, Is.False);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [Test]
    public void DropdownEscapePrecedesPopupEscape()
    {
        var (vm, row, view) = Create(new Track());
        using (view) try
        {
            row.OpenSampleConfiguration();
            Assert.That(row.HandleSampleConfigurationEscape(dropdownIsOpen: true), Is.False);
            Assert.That(row.IsSampleConfigurationOpen, Is.True);
            Assert.That(row.HandleSampleConfigurationEscape(dropdownIsOpen: false), Is.True);
            Assert.That(vm.IsSampleConfigurationOpen, Is.False);
        }
        finally { row.CloseSampleConfiguration(); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [Test]
    public void OpeningClearsPlacementPreviewAndBlocksEnter()
    {
        var track = new Track { SelectedSampleFileName = "a.wav" }; var (vm, row, view) = Create(track);
        using (view) try
        {
            var grid = row.SampleTimeline!;
            grid.ContentWidth = 1000; grid.ViewportWidth = 500; grid.Duration = TimeSpan.FromSeconds(10);
            grid.SetActivePosition(125); view.UpdateLayout();
            Assert.That(grid.HasPlacementPreview, Is.True);
            row.OpenSampleConfiguration();
            Assert.That(grid.HasPlacementPreview, Is.False);
            Assert.That(grid.RequestSamplePlacement(), Is.False);
            Assert.That(track.Samples, Is.Empty);
            row.CloseSampleConfiguration();
            Assert.That(grid.RequestSamplePlacement(), Is.True);
            Assert.That(track.Samples, Has.Count.EqualTo(1));
        }
        finally { row.CloseSampleConfiguration(); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [Test]
    public void DismissalInputCannotPlaceButNextGestureCan()
    {
        var track = new Track { SelectedSampleFileName = "a.wav" };
        var (vm, row, view) = Create(track);
        using (view) try
        {
            row.OpenSampleConfiguration();
            Assert.That(vm.PlaceSample(track, 125), Is.False);
            row.CloseSampleConfiguration(false);
            Assert.That(vm.PlaceSample(track, 125), Is.False, "Outside dismissal retains guard through input completion.");
            row.CompleteSampleConfigurationDismissal();
            Assert.That(vm.PlaceSample(track, 125), Is.True);
        }
        finally { row.CompleteSampleConfigurationDismissal(); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [Test]
    public void OwnerReplacementAndUnloadCloseWithoutLeakingGuard()
    {
        var track = new Track(); var (vm, row, view) = Create(track);
        try
        {
            row.OpenSampleConfiguration();
            var newer = vm.BeginSampleConfiguration(track)!.Value;
            Assert.That(row.IsSampleConfigurationOpen, Is.False);
            row.CompleteSampleConfigurationDismissal();
            Assert.That(vm.IsSampleConfigurationOpen, Is.True);
            vm.EndSampleConfiguration(newer);
            row.OpenSampleConfiguration(); view.Dispose();
            Assert.That(vm.IsSampleConfigurationOpen, Is.False);
        }
        finally { view.Dispose(); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(false, true)]
    public void OutsideDismissalRecoversForEitherReleaseOrdering(bool releaseBeforeClosed, bool releaseConsumed)
    {
        var track = new Track { SelectedSampleFileName = "a.wav" };
        var (vm, row, view) = Create(track);
        using (view) try
        {
            row.OpenSampleConfiguration();
            // Presentation alone is substituted; closing now follows the native dismissal lifecycle.
            row.PresentSampleConfiguration = null;
            row.CloseSampleConfiguration(false, anyMouseButtonPressed: !releaseBeforeClosed);
            Assert.That(vm.CanCreateSampleNodes, Is.False);
            Assert.That(vm.PlaceSample(track, 125), Is.False);
            if (!releaseBeforeClosed && !releaseConsumed)
                Assert.That(row.HandleConfigurationDismissalInput(Mouse.PreviewMouseUpEvent,
                    MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released), Is.True);
            else if (releaseConsumed)
                Assert.That(row.HandleConfigurationDismissalInput(Mouse.PreviewMouseMoveEvent,
                    MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released), Is.False);
            PumpDismissal(row);
            Assert.That(track.Samples, Is.Empty);
            Assert.That(vm.CanCreateSampleNodes, Is.True, "Released input must restore placement without another click.");
        }
        finally { row.CompleteSampleConfigurationDismissal(); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    private static void PumpDismissal(OHsWorkstationTrackRow row) => UiTestPump.Run(
        row.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);

    [Test]
    public void DismissalMovementCannotStartPlacement()
    {
        var track = new Track { SelectedSampleFileName = "a.wav" };
        var (vm, row, view) = Create(track);
        using (view) try
        {
            row.OpenSampleConfiguration(); row.PresentSampleConfiguration = null;
            row.CloseSampleConfiguration(false, anyMouseButtonPressed: true);
            Assert.That(DismissalInput(row, Mouse.PreviewMouseMoveEvent, 1), Is.False);
            PumpDismissal(row);
            Assert.That(vm.CanCreateSampleNodes, Is.False);
            Assert.That(vm.PlaceSample(track, 125), Is.False);
            Assert.That(track.Samples, Is.Empty);
            DismissalInput(row, Mouse.PreviewMouseMoveEvent, 0); PumpDismissal(row);
            Assert.That(vm.CanCreateSampleNodes, Is.True);
        }
        finally { row.CompleteSampleConfigurationDismissal(); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    [TestCase(3)]
    public void DismissalWaitsForHeldButtons(int heldButtons)
    {
        var (vm, row, view) = Create(new Track());
        using (view) try
        {
            row.OpenSampleConfiguration(); row.PresentSampleConfiguration = null;
            row.CloseSampleConfiguration(false, anyMouseButtonPressed: false);
            // A new press must cancel completion already queued by a previous release.
            Assert.That(DismissalInput(row, Mouse.PreviewMouseDownEvent, heldButtons), Is.True);
            PumpDismissal(row);
            Assert.That(vm.CanCreateSampleNodes, Is.False);
            Assert.That(DismissalInput(row, Mouse.PreviewMouseUpEvent, heldButtons), Is.True);
            PumpDismissal(row);
            Assert.That(vm.CanCreateSampleNodes, Is.False);
            Assert.That(DismissalInput(row, Mouse.PreviewMouseUpEvent, 0), Is.True);
            PumpDismissal(row);
            Assert.That(vm.CanCreateSampleNodes, Is.True);
            Assert.That(DismissalInput(row, Mouse.PreviewMouseDownEvent, 1), Is.False);
        }
        finally { row.CompleteSampleConfigurationDismissal(); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [Test]
    public void PendingDismissalCannotReleaseReplacementOwner()
    {
        var track = new Track(); var (vm, row, view) = Create(track);
        using (view) try
        {
            row.OpenSampleConfiguration(); row.PresentSampleConfiguration = null;
            row.CloseSampleConfiguration(false, anyMouseButtonPressed: false);
            var replacement = vm.BeginSampleConfiguration(track)!.Value;
            PumpDismissal(row);
            Assert.That(vm.OwnsSampleConfiguration(replacement), Is.True);
            Assert.That(DismissalInput(row, Mouse.PreviewMouseDownEvent, 1), Is.False);
            vm.EndSampleConfiguration(replacement);
        }
        finally { row.CompleteSampleConfigurationDismissal(); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [TestCase("recycle")]
    [TestCase("unload")]
    [TestCase("deactivate")]
    public void InvalidatedDismissalCannotConsumeLaterInput(string invalidation)
    {
        var (vm, row, view) = Create(new Track());
        using (view) try
        {
            row.OpenSampleConfiguration(); row.PresentSampleConfiguration = null;
            row.CloseSampleConfiguration(false, anyMouseButtonPressed: false);
            switch (invalidation)
            {
                case "recycle": row.Track = new Track(); break;
                case "unload": view.Dispose(); break;
                case "deactivate": UiTestPump.Run(vm.DeactivateAsync()); break;
            }
            PumpDismissal(row);
            Assert.That(vm.IsSampleConfigurationOpen, Is.False);
            Assert.That(DismissalInput(row, Mouse.PreviewMouseDownEvent, 1), Is.False);
        }
        finally { row.CompleteSampleConfigurationDismissal(); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    internal static bool DismissalInput(OHsWorkstationTrackRow row, RoutedEvent routedEvent, int heldButtons) =>
        row.HandleConfigurationDismissalInput(routedEvent,
            (heldButtons & 1) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (heldButtons & 2) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (heldButtons & 4) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released);

    [Test]
    public void EmptyAndMissingFilesRetainSelectionAndGateCreation()
    {
        var track = new Track { SelectedSampleFileName = "missing.wav" }; var (vm, row, view) = Create(track);
        using (view) try
        {
            vm.SampleFiles.Clear(); row.OpenSampleConfiguration();
            Assert.That(((ComboBox)row.FindName("TrackSampleFileSelector")).SelectedItem, Is.Null);
            Assert.That(track.SelectedSampleFileName, Is.EqualTo("missing.wav"));
            Assert.That(vm.PlaceSample(track, 125), Is.False);
        }
        finally { row.CloseSampleConfiguration(); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
