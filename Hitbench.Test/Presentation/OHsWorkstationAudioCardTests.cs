using Hitbench.Views.Pages;
using Hitbench.ViewModels.Shell;
using Hitbench.Core.Entities;
using Hitbench.Resources.CustomControls;
using Hitbench.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationAudioCardTests
{
    [Test]
    public void CompactProjectMenuKeepsAuthoritativeCommandsAndDeactivatesWhenRoomy()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var page = WorkstationTestPage.Create();
        var vm = (WorkstationPageVM)page.DataContext;
        var card = new OHsWorkstationAudioCard { DataContext = vm, IsCompactLayout = true };
        try
        {
            UiTestPump.Layout(card, 640, 300);
            var trigger = card.FindName("ProjectActionsButton") as BalloonComboBox;
            Assert.That(trigger, Is.Not.Null);
            var menu = trigger!.ContextMenu;
            trigger.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            UiTestPump.Layout(menu, 320, 300);
            var commands = menu.Items.Cast<MenuItem>().Select(item => item.Command).ToArray();
            Assert.That(commands, Is.EquivalentTo(new System.Windows.Input.ICommand[]
            {
                vm.ResnapAllSamplesCommand, vm.ReloadSamplesCommand, vm.ToggleTracksCollapsedCommand,
                vm.OpenBeatmapFolderCommand, vm.OpenProjectFolderCommand
            }));
            Assert.That(((BalloonButton)card.FindName("ExportButton")).Command, Is.SameAs(vm.ExportCommand));
            Assert.That(menu.Items.Cast<MenuItem>().All(item => !item.IsEnabled), Is.True);
            // Borrow the real resource through the existing native-presentation substitute.
            trigger.ContextMenu = null;
            var owner = new StubBalloonComboBox { DataContext = vm, ContextMenu = menu };
            owner.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(card.IsCompactLayout)) { Source = card, Mode = BindingMode.OneWay });
            owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            owner.Activate();
            Assert.That(owner.IsDropDownOpen, Is.True);
            card.IsCompactLayout = false;
            Assert.That(owner.IsDropDownOpen, Is.False);
            Assert.That(owner.IsMenuOwnerActive, Is.False);
            Assert.That(owner.PresentationRequests.Last().Open, Is.False);
            owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    [Test]
    public void ExportUsesExistingToolbarContractAndTracksCommandAvailability()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var dialogs = new StubOHsExportDialogService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), exportDialog: dialogs);
        try
        {
            var card = new OHsWorkstationAudioCard { DataContext = vm };
            UiTestPump.Layout(card, 900, 300);
            var button = (BalloonButton)card.FindName("ExportButton");
            UiControlContract.AssertStyle<BalloonButton>(button, "OHsWorkstationToolbarButtonStyle");
            Assert.That(button.ButtonIcon, Is.SameAs(card.FindResource("Export")));
            Assert.That(AutomationProperties.GetName(button), Is.EqualTo("Export"));
            Assert.That(button.Command, Is.SameAs(vm.ExportCommand)); Assert.That(button.IsEnabled, Is.False);
            UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            UiTestPump.Layout(card, 900, 300); Assert.That(button.IsEnabled, Is.True);
            UiTestPump.Run(vm.ExportCommand.ExecuteAsync(null)); Assert.That(dialogs.Calls, Is.EqualTo(1));
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }
    [Test]
    public void ControlPointAddIsSeparateFromTrackAddMenu()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        var card = new OHsWorkstationAudioCard { DataContext = vm };
        UiTestPump.Layout(card, 900, 250);
        var add = (BalloonComboBox)card.FindName("AddMenuButton");
        Assert.That(add.ContextMenu.Items.Count, Is.EqualTo(2));
        var plus = (BalloonButton)card.FindName("AddControlPointButton");
        Assert.That(plus.Command, Is.SameAs(vm.AddControlPointCommand));
        Assert.That(card.ContainsInputOrigin((DependencyObject)card.FindName("PlayPauseButton")), Is.False);
        UiTestPump.Run(vm.DeactivateAsync());
    }
    [Test]
    public void ElapsedTimeFollowsExistingAuthority()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var audio = new StubWorkstationAudioService
        {
            Analyze = (_, _) => Task.FromResult(new WaveformData(TimeSpan.FromMinutes(70), [.5f]))
        };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler());
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json", default));
            vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var card = new OHsWorkstationAudioCard { DataContext = vm };
            var footer = new OHsWorkstationFooter { DataContext = vm };
            UiTestPump.Layout(card, 900, 300);
            UiTestPump.Layout(footer, 900, 100);
            UiTestPump.Run(vm.SeekCommand.ExecuteAsync(TimeSpan.FromMinutes(65.25)));
            var elapsed = card.FindName("ElapsedTimeText") as TextBlock;
            Assert.That(elapsed, Is.Not.Null);
            Assert.That(elapsed!.Text, Is.EqualTo("65:15.000"));
            Assert.That(BindingOperations.GetBinding(elapsed, TextBlock.TextProperty)!.Mode, Is.EqualTo(BindingMode.OneWay));
            var bindings = Descendants(card).Concat(Descendants(footer)).OfType<TextBlock>()
                .Count(text => BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path.Path == nameof(vm.ElapsedText));
            Assert.That(bindings, Is.EqualTo(1));
            UiControlContract.AssertStyle<TextBlock>(elapsed, "OHsWorkstationElapsedStyle");
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject owner)
    {
        yield return owner;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(owner); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(owner, index))) yield return child;
    }

    [Test]
    public void AddMenuBindsAuthoritativeCommandsAndReadiness()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(),
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["hit.wav"]));
        var card = new OHsWorkstationAudioCard { DataContext = vm };
        try
        {
            UiTestPump.Layout(card, 900, 300);
            var trigger = card.FindName("AddMenuButton") as BalloonComboBox;
            Assert.That(trigger, Is.Not.Null);
            trigger!.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            UiTestPump.Layout(trigger.ContextMenu, 320, 200);
            Drain();
            var items = trigger.ContextMenu.Items.Cast<MenuItem>().ToArray();
            Assert.That(items.Select(item => item.Header), Is.EqualTo(new[] { "Sample Track", "Instrumental Track" }));
            Assert.That(items.Select(item => item.Command), Is.EqualTo(new System.Windows.Input.ICommand[]
                { vm.AddSampleTrackCommand, vm.AddPianoTrackCommand }));
            UiControlContract.AssertStyle<BalloonComboBox>(trigger, "OHsWorkstationAddActionStyle");
            Assert.That(trigger.Style.BasedOn, Is.SameAs(trigger.FindResource("BaseBalloonComboBoxStyle")));
            foreach (var item in items) UiControlContract.AssertStyle<MenuItem>(item, "OHsActionMenuItemStyle");
            Assert.That(items.All(item => !item.IsEnabled), Is.True);
            UiTestPump.Run(vm.LoadAsync("project.json", default));
            vm.AcknowledgePresentationReady(vm.LoadGeneration);
            Drain();
            Assert.That(items[0].IsEnabled, Is.True);
            Assert.That(items[1].IsEnabled, Is.True);
            vm.IsReturning = true;
            Drain();
            Assert.That(items.All(item => !item.IsEnabled), Is.True);
            Assert.That(trigger.IsEnabled, Is.True, "Each action owns availability independently.");
        }
        finally { card.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [Test]
    public void MenuRebindingUsesCurrentCommandsAndUnloadedOwnersCannotInvoke()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var card = new OHsWorkstationAudioCard();
        var trigger = card.FindName("AddMenuButton") as BalloonComboBox;
        Assert.That(trigger, Is.Not.Null);
        var menu = trigger!.ContextMenu;
        trigger.ContextMenu = null;
        var oldCalls = 0; var currentCalls = 0;
        var oldCommand = new RelayCommand(() => oldCalls++);
        var currentCommand = new RelayCommand(() => currentCalls++);
        var stub = new StubBalloonComboBox { ContextMenu = menu, DataContext = new
            { AddSampleTrackCommand = oldCommand, AddPianoTrackCommand = oldCommand, AddControlPointCommand = oldCommand } };
        stub.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        UiTestPump.Layout(menu, 320, 200);
        try
        {
            stub.Activate();
            var item = (MenuItem)menu.Items[0];
            var provider = (IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke);
            provider.Invoke(); Drain();
            Assert.That(oldCalls, Is.EqualTo(1));
            stub.DataContext = new { AddSampleTrackCommand = currentCommand, AddPianoTrackCommand = currentCommand, AddControlPointCommand = currentCommand };
            Drain();
            Assert.That(item.Command, Is.SameAs(currentCommand));
            stub.Activate(); provider.Invoke(); Drain();
            Assert.That(oldCalls, Is.EqualTo(1)); Assert.That(currentCalls, Is.EqualTo(1));
            stub.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Drain();
            Assert.Throws<ElementNotEnabledException>(() => provider.Invoke());
            Assert.That(menu.IsOpen, Is.False);
        }
        finally { stub.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    }

    [Test]
    public void WorkstationSegmentsPreserveIndependentModesAndSelection()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var node = new Sample { FileName = "hit.wav", Timestamp = 125 };
        var track = new Track { SelectedSampleFileName = "hit.wav", Samples = [node] };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["hit.wav"]));
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new InstrumentalTrackSnapshot(context, "Piano", 100, new Dictionary<string, string>(), [], null, 0);
        var editor = new InstrumentalArrangementDrawerVM(InstrumentalArrangementTestContext.Create(snapshot, [0], [120], TimeSpan.Zero, 4, 1),
            new WeakReferenceMessenger(), new ImmediateUiScheduler());
        try
        {
            var card = new OHsWorkstationAudioCard { DataContext = vm };
            using var view = new InMemoryView(new Window { Content = card, Width = 900, Height = 300 }, processFrames: false, scheduleLayout: false);
            UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            vm.SelectNode(track, node, false);
            var group = card.FindName("EditingModeGroup") as SegmentedToggleGroup;
            Assert.That(group, Is.Not.Null);
            var select = (SegmentedToggleOption)card.FindName("SelectionModeButton");
            var draw = (SegmentedToggleOption)card.FindName("AdditionModeButton");
            UiControlContract.AssertStyle<SegmentedToggleGroup>(group!, typeof(SegmentedToggleGroup));
            UiControlContract.AssertStyle<SegmentedToggleOption>(select, typeof(SegmentedToggleOption));
            var selected = vm.SelectedTrack;
            var editorMode = editor.IsAdditionMode;
            select.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            Assert.That(vm.IsAdditionMode, Is.False); Assert.That(editor.IsAdditionMode, Is.EqualTo(editorMode));
            editor.RequestEditingMode(!editorMode);
            Assert.That(vm.IsAdditionMode, Is.False);
            draw.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            Assert.That(vm.IsAdditionMode, Is.True); Assert.That(vm.SelectedTrack, Is.SameAs(selected));
            Assert.That(vm.SelectedSamples, Is.EqualTo(new[] { node }));
            Assert.That(BindingOperations.GetBinding(group!, SegmentedToggleGroup.SelectedValueProperty)!.Mode, Is.EqualTo(BindingMode.TwoWay));
        }
        finally { UiTestPump.Run(editor.DeactivateAsync()); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [Test]
    public void CardTransportConsumesDrawerAvailabilityAndSamplesReplacesMapping()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var audio = new StubWorkstationAudioService();
        var pianoOpened = new TaskCompletionSource(); var mappingOpened = new TaskCompletionSource();
        var sampleRequests = 0;
        var dialogs = new StubInstrumentalTrackDialogService
        {
            Arrangement = async (_, token) => { pianoOpened.TrySetResult(); await Task.Delay(Timeout.Infinite, token); },
            Mapping = async (_, token) => { mappingOpened.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
        };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(),
            new StubSampleFileService([]), new StubSampleFileDialogService(Array.Empty<string>()), messenger: new WeakReferenceMessenger(),
            instrumentDialogs: dialogs, sampleManagementDialog: new StubSampleManagementDialogService
                { Present = (_, _) => { sampleRequests++; return Task.CompletedTask; } });
        var card = new OHsWorkstationAudioCard { DataContext = vm };
        using var view = new InMemoryView(new Window { Content = card, Width = 900, Height = 300 }, processFrames: false, scheduleLayout: false);
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            UiTestPump.Run(vm.PlayPauseCommand.ExecuteAsync(null));
            var piano = vm.ViewArrangementCommand.ExecuteAsync(track); UiTestPump.Run(pianoOpened.Task.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.That(audio.Snapshot.IsPlaying, Is.True);
            AssertTransport(card, vm, true);
            var mapping = vm.KeysMappingCommand.ExecuteAsync(track); UiTestPump.Run(mappingOpened.Task.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.That(audio.Snapshot.IsPlaying, Is.False);
            AssertTransport(card, vm, false);
            var samples = (BalloonButton)card.FindName("ManageSamplesButton");
            Assert.That(samples.Command, Is.SameAs(vm.ManageSamplesCommand));
            UiTestPump.Run(vm.ManageSamplesCommand.ExecuteAsync(null));
            UiTestPump.Run(Task.WhenAll(piano, mapping));
            Assert.That(sampleRequests, Is.EqualTo(1));
            AssertTransport(card, vm, true);
            Assert.That(audio.Snapshot.IsPlaying, Is.False);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CardAvailabilityRecoversAfterMappingFailureOrCancellation(bool fail)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var opened = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            messenger: new WeakReferenceMessenger(), instrumentDialogs: new StubInstrumentalTrackDialogService
            { Mapping = async (_, token) => { opened.TrySetResult(); await release.Task.WaitAsync(token); } });
        var card = new OHsWorkstationAudioCard { DataContext = vm };
        using var view = new InMemoryView(new Window { Content = card, Width = 900, Height = 300 }, processFrames: false, scheduleLayout: false);
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var mapping = vm.KeysMappingCommand.ExecuteAsync(track);
            UiTestPump.Run(opened.Task.WaitAsync(TimeSpan.FromSeconds(3)));
            AssertTransport(card, vm, false);
            if (fail) release.SetException(new IOException("mapping failed"));
            else UiTestPump.Run(vm.SettleInstrumentalEditorAsync());
            UiTestPump.Run(mapping);
            AssertTransport(card, vm, true);
            Assert.That(vm.IsPlaying, Is.False);
            if (fail) Assert.That(vm.ErrorMessage, Is.EqualTo("mapping failed"));
            UiTestPump.Run(vm.DeactivateAsync());
            AssertTransport(card, vm, false);
        }
        finally { release.TrySetResult(); UiTestPump.Run(vm.DeactivateAsync()); }
    }

    private static void AssertTransport(OHsWorkstationAudioCard card, WorkstationPageVM vm, bool enabled)
    {
        Drain();
        Assert.That(((BalloonButton)card.FindName("PlayPauseButton")).IsEnabled, Is.EqualTo(enabled));
        Assert.That(Descendants(card).OfType<BalloonButton>().Single(button => ReferenceEquals(button.Command, vm.StopCommand)).IsEnabled, Is.EqualTo(enabled));
        Assert.That(((BalloonCheckBox)card.FindName("ScrollWithMarkerCheckBox")).IsEnabled, Is.EqualTo(enabled));
    }

    private static void Drain() => UiTestPump.Run(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);

    [Test]
    public void ComponentUsesExistingViewModelContract()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        var owner = new OHsWorkstationAudioCard { DataContext = vm };
        UiTestPump.Layout(owner, 900, 250);
        var trigger = owner.FindName("AddMenuButton") as BalloonComboBox;
        Assert.That(trigger, Is.Not.Null);
        trigger!.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Drain();
        var action = (MenuItem)trigger.ContextMenu.Items[0];
        Assert.That(action.Command, Is.SameAs(vm.AddSampleTrackCommand));
        Assert.That(owner.Timeline.DataContext, Is.SameAs(vm));
        UiTestPump.Run(vm.DeactivateAsync());
    }
}
