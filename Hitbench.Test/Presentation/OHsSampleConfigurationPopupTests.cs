using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Views.Pages;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsSampleConfigurationPopupTests
{
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
    public void MenuOffersConfigOnlyForSampleTracks(bool instrumental)
    {
        var (vm, row, view) = Create(instrumental ? new InstrumentalTrack() : new Track());
        using (view) try
        {
            var menu = Descendants(row).OfType<Hitbench.Resources.CustomControls.BalloonComboBox>().Single().ContextMenu;
            Assert.That(menu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "Config Sample") && item.Visibility == Visibility.Visible), Is.EqualTo(!instrumental));
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
            file.SelectedItem = "b.wav"; addition.SelectedItem = "Soft"; type.SelectedItem = SampleType.Finish;
            Assert.That(track.SelectedSampleFileName, Is.EqualTo("b.wav"));
            Assert.That(track.Addition, Is.EqualTo("Soft")); Assert.That(track.SampleType, Is.EqualTo(SampleType.Finish));
            vm.SampleFiles.Clear(); vm.SampleFiles.Add("b.wav"); row.RefreshPresentation();
            Assert.That(file.SelectedItem, Is.EqualTo("b.wav"));
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
            var config = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Config Sample"));
            config.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, config));
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
