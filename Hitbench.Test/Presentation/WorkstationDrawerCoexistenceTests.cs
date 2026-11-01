using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.ViewModels.Popups;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;
using Hitbench.Views.Popups;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.Services.Dialogs;
using Hitbench.ViewModels.Shell;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class WorkstationDrawerCoexistenceTests
{
    private MainWindow shell = null!;
    private WorkstationPage page = null!;
    private InMemoryView view = null!;
    private InstrumentalArrangementDrawerVM arrangement = null!;
    private ManageSamplesPopupVM manager = null!;
    private OHsInstrumentalArrangementDrawer piano = null!;
    private ManageSamplesDrawer samples = null!;
    private CancellationTokenSource lifetime = null!;
    private readonly List<Task> presentations = [];

    [SetUp]
    public void SetUp()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var messenger = new WeakReferenceMessenger(); var settings = new StubAppSettingsService();
        HomePage CreateHome() => new(new HomePageVM(new StubHomeFolderService(), settings, new StubInstallationDialogService(),
            new ImmediateUiScheduler(), new StubProjectFileService((_, _, _) => Task.FromResult("project.json")), messenger));
        var home = CreateHome();
        page = WorkstationTestPage.Create();
        shell = TestShell.Create(home, messenger, () => page, (HomePageVM)home.DataContext, settings, createHome: CreateHome);
        shell.Receive(new ProjectOpenRequestedMessage("project.json"));
        view = new InMemoryView(shell); UiTestPump.Run(page.LoadCompletion);
        view.Root.Resources.MergedDictionaries.Add(shell.Resources);
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var note = new InstrumentalNoteSnapshot(Guid.NewGuid(), "C4", 125);
        arrangement = new(InstrumentalArrangementTestContext.Create(new(context, "Piano", 100,
            new Dictionary<string, string> { ["C4"] = "piano.wav" }, [note], ["piano.wav"], 0), [0], [120], TimeSpan.FromSeconds(10), 4, 32),
            messenger, new ImmediateUiScheduler());
        UiTestPump.Run(arrangement.InitializeAsync(default)); piano = new(arrangement);
        var files = new StubSampleFileService(["piano.wav"]);
        manager = new(new("project.json", null, context.ProjectSessionId), files, new(files), new StubSampleFileDialogService([]),
            new StubSampleRemovalDialogService(), messenger, new ImmediateUiScheduler(), new StubSamplePreviewService());
        UiTestPump.Run(manager.InitializeAsync(default)); samples = new(manager);
        lifetime = new(); presentations.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        lifetime.Cancel(); shell.DismissSampleManager(samples); shell.DismissSampleManager(piano);
        foreach (var task in presentations)
            UiTestPump.Run(task.ContinueWith(completed => { _ = completed.Exception; }, TaskScheduler.Default));
        UiTestPump.Run(manager.DeactivateAsync()); UiTestPump.Run(arrangement.DeactivateAsync());
        UiTestPump.Run(page.DeactivateAsync()); view.Dispose(); shell.Close(); lifetime.Dispose();
    }

    private Task ShowPiano(Func<bool, CancellationToken, Task>? transition = null)
    {
        var task = shell.PresentDrawerAsync(piano, lifetime.Token, transition ?? ((_, _) => Task.CompletedTask), true);
        presentations.Add(task);
        ((Grid)shell.FindName("SampleManagerLayer")).Visibility = Visibility.Visible;
        view.UpdateLayout();
        return task;
    }

    private Task ShowSamples(Func<bool, CancellationToken, Task>? transition = null)
    {
        var task = shell.PresentSampleManagerAsync(samples, lifetime.Token, transition ?? ((_, _) => Task.CompletedTask));
        presentations.Add(task); view.UpdateLayout(); return task;
    }

    private (WorkstationPageVM Owner, InstrumentalTrack Track, Task PianoOpened, Task ManagerOpened) UseCoordinatedDrawers(string importOutcome = "accept")
    {
        UiTestPump.Run(page.DeactivateAsync()); UiTestPump.Run(arrangement.DeactivateAsync()); UiTestPump.Run(manager.DeactivateAsync());
        var messenger = new WeakReferenceMessenger();
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" }, Notes = [new() { Key = "C4", Timestamp = 125 }] };
        IReadOnlyList<string> names = ["piano.wav"];
        var files = new StubSampleFileService(names)
        {
            List = (_, _) => Task.FromResult(names),
            Rename = (_, _, stem, _) => { names = [stem + ".wav"]; return Task.FromResult(stem + ".wav"); },
            Import = (_, _, _) => importOutcome == "failure" ? Task.FromException<string>(new IOException("copy failed")) :
                Task.FromResult(AddImported())
        };
        string AddImported() { names = [.. names, "new.wav"]; return "new.wav"; }
        var picker = new StubSampleFileDialogService(importOutcome == "cancel" ? [] : new[] { "new.wav" });
        var pianoOpened = new TaskCompletionSource(); var managerOpened = new TaskCompletionSource();
        var service = new SampleManagementDialogService(() => shell, shell.Dispatcher,
            () => throw new AssertionException("Native picker unexpectedly created"),
            (context, _, _) => new(context, files, new(files), picker, new StubSampleRemovalDialogService(), messenger,
                new ImmediateUiScheduler(), new StubSamplePreviewService()),
            async (vm, token) =>
            {
                manager = vm; samples = new(vm);
                var showing = shell.PresentSampleManagerAsync(samples, token, (_, _) => Task.CompletedTask);
                managerOpened.TrySetResult(); await showing;
            }, (_, _) => shell.RequestSampleManagerCloseAsync(samples));
        var dialogs = new StubInstrumentalTrackDialogService { Arrangement = async (data, token) =>
        {
            var vm = new InstrumentalArrangementDrawerVM(data, messenger, new ImmediateUiScheduler());
            arrangement = vm; await vm.InitializeAsync(token); piano = new(vm);
            try
            {
                var showing = shell.PresentDrawerAsync(piano, token, (_, _) => Task.CompletedTask, true);
                pianoOpened.TrySetResult(); await showing;
            }
            finally { await vm.DeactivateAsync(); }
        } };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song",
            new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120], ControlPoints = [new()] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), files, picker,
            messenger: messenger, sampleManagementDialog: service, instrumentDialogs: dialogs);
        page = new(owner);
        ((ContentControl)shell.FindName("PageHost")).Content = page;
        UiTestPump.Run(owner.LoadAsync("project.json", default)); owner.AcknowledgePresentationReady(owner.LoadGeneration); view.UpdateLayout();
        return (owner, track, pianoOpened.Task, managerOpened.Task);
    }

    [TestCase(false)] [TestCase(true)]
    public void LauncherAndAuthoritativeSampleRefreshPreserveLiveArrangement(bool expanded)
    {
        var (owner, track, pianoOpened, managerOpened) = UseCoordinatedDrawers();
        var editing = owner.ViewArrangementCommand.ExecuteAsync(track); UiTestPump.Run(pianoOpened);
        if (expanded) page.TogglePianoRollExpansion(false);
        arrangement.SetNoteSelection([arrangement.Notes.Single().Id], arrangement.Notes.Single().Id);
        var selected = arrangement.SelectedNoteIds.ToArray();
        var samePiano = piano; var sameArrangement = arrangement;
        var roll = (PianoRollView)piano.FindName("PianoRoll"); roll.VerticalOffset = 500;
        var pitch = roll.VerticalOffset;
        var button = (BalloonButton)page.Toolbar.FindName("ManageSamplesButton");
        Assert.That(shell.HandleWindowKeyDown(true, Key.F2, ModifierKeys.None, false, roll), Is.True);
        UiTestPump.Run(managerOpened); view.UpdateLayout();
        Assert.That(owner.ManageSamplesCommand.CanExecute(null), Is.True);
        Assert.That(AutomationProperties.GetItemStatus(button), Is.EqualTo("Open"));
        Assert.That(button.ButtonIcon, Is.SameAs(button.FindResource("Close")));
        Assert.That(shell.HandleWindowKeyDown(true, Key.Space, ModifierKeys.None, false, samples), Is.False);
        var follow = owner.ScrollWithMarker;
        Assert.That(shell.HandleWindowKeyDown(true, Key.F, ModifierKeys.None, false, roll), Is.True);
        Assert.That(owner.ScrollWithMarker, Is.EqualTo(!follow));
        foreach (var control in WorkstationControlTree.Descendants(page).OfType<Control>())
            Assert.That(control.IsTabStop, Is.False, control.GetType().Name);
        UiTestPump.Run(manager.EditCommand.ExecuteAsync(manager.Entries.Single())); manager.DraftStem = "renamed";
        UiTestPump.Run(manager.CommitRenameCommand.ExecuteAsync(null));
        Assert.That(track.KeyFiles["C4"], Is.EqualTo("renamed.wav"));
        Assert.That(arrangement.SelectedNoteIds, Is.EqualTo(selected)); Assert.That(arrangement.Notes, Has.Count.EqualTo(1));
        Assert.That(shell.HandleWindowKeyDown(true, Key.F2, ModifierKeys.None, false, roll), Is.True);
        UiTestPump.Run(shell.DrawerInputCompletion);
        UiTestPump.Run(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);
        Assert.That(owner.IsSamplesOpen, Is.False); Assert.That(editing.IsCompleted, Is.False);
        Assert.That(AutomationProperties.GetItemStatus(button), Is.EqualTo("Closed"));
        Assert.That(button.ButtonIcon, Is.SameAs(button.FindResource("MusicNote")));
        Assert.That(piano, Is.SameAs(samePiano)); Assert.That(arrangement, Is.SameAs(sameArrangement));
        Assert.That(roll.VerticalOffset, Is.EqualTo(pitch)); Assert.That(page.IsPianoRollExpanded, Is.EqualTo(expanded));
        UiTestPump.Run(owner.SettleWorkstationDrawersAsync()); UiTestPump.Run(editing); UiTestPump.Run(owner.FlushSavesAsync());
    }

    [TestCase("accept")] [TestCase("cancel")] [TestCase("failure")]
    public void ManagerImportReleasesBusyStateWithoutCancellingExpandedPiano(string outcome)
    {
        var (owner, track, pianoOpened, managerOpened) = UseCoordinatedDrawers(outcome);
        var editing = owner.ViewArrangementCommand.ExecuteAsync(track); UiTestPump.Run(pianoOpened);
        page.TogglePianoRollExpansion(false);
        UiTestPump.Run(owner.ManageSamplesCommand.ExecuteAsync(null)); UiTestPump.Run(managerOpened);
        var samePiano = piano;
        UiTestPump.Run(manager.AddNewSampleCommand.ExecuteAsync(null));
        Assert.That(manager.CanClose, Is.True); Assert.That(manager.IsBusy, Is.False);
        Assert.That(owner.CanImportDroppedSamples, Is.False);
        Assert.That(editing.IsCompleted, Is.False); Assert.That(piano, Is.SameAs(samePiano));
        Assert.That(page.IsPianoRollExpanded, Is.True); Assert.That(arrangement.CanEdit, Is.True);
        Assert.That(owner.SampleFiles.Contains("new.wav"), Is.EqualTo(outcome == "accept"));
        if (outcome == "failure") Assert.That(manager.ErrorMessage, Does.Contain("copy failed"));
        UiTestPump.Run(owner.SettleWorkstationDrawersAsync()); UiTestPump.Run(editing);
    }

    [Test]
    public void EscapeCancelsSamplesRenameBeforeClosingAndRespectsModalFocus()
    {
        var editing = ShowPiano(); var managing = ShowSamples();
        Assert.That(shell.ShouldRestoreDrawerFocus(false, true), Is.True);
        Assert.That(shell.ShouldRestoreDrawerFocus(false, false), Is.False);
        Assert.That(shell.ShouldRestoreDrawerFocus(true, true), Is.False);
        shell.ShowPopupOverlay();
        Assert.That(shell.ShouldRestoreDrawerFocus(false, true), Is.False);
        Assert.That(shell.RequestSampleManagerCloseAsync(samples).Result, Is.False);
        shell.HidePopupOverlay();
        UiTestPump.Run(manager.EditCommand.ExecuteAsync(manager.Entries.Single()));
        manager.DraftStem = "unfinished";
        Assert.That(shell.HandleWindowKeyDown(true, Key.Escape, ModifierKeys.None, false, samples), Is.False);
        Assert.That(samples.HandleEscape(), Is.True);
        Assert.That(manager.ActiveEdit, Is.Null);
        Assert.That(managing.IsCompleted, Is.False);
        Assert.That(editing.IsCompleted, Is.False);
        samples.HandleEscape(); UiTestPump.Run(managing);
        Assert.That(editing.IsCompleted, Is.False);
    }

    [Test]
    public void SamplesLauncherSupportsAutomationInvocation()
    {
        var (owner, _, _, managerOpened) = UseCoordinatedDrawers();
        var button = (BalloonButton)page.Toolbar.FindName("ManageSamplesButton");
        var peer = UIElementAutomationPeer.CreatePeerForElement(button);
        Assert.That(peer, Is.Not.Null);
        var provider = peer!.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        Assert.That(provider, Is.Not.Null);
        provider!.Invoke(); UiTestPump.Run(managerOpened);
        Assert.That(owner.IsSamplesOpen, Is.True);
        provider.Invoke();
        UiTestPump.Run(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);
        Assert.That(owner.IsSamplesOpen, Is.False);
        button.IsEnabled = false;
        Assert.That(() => provider.Invoke(), Throws.TypeOf<ElementNotEnabledException>());
        UiTestPump.Run(owner.SettleWorkstationDrawersAsync());
    }

    [TestCase(false, false)] [TestCase(true, false)] [TestCase(true, true)]
    public void ClosingEitherDrawerPreservesOtherPresentation(bool expanded, bool closePiano)
    {
        var editing = ShowPiano();
        if (expanded) page.TogglePianoRollExpansion(false);
        var managing = ShowSamples();
        Assert.That(managing.IsFaulted, Is.False);
        Assert.That(editing.IsCompleted, Is.False);
        var sampleHost = (ContentControl)shell.FindName("SampleManagerHost");
        var pianoHost = (ContentControl)shell.FindName("InstrumentalDrawerHost");
        UiControlContract.AssertStyle<ContentControl>(sampleHost, "OHsMainWindowShellDrawerHostStyle");
        UiControlContract.AssertStyle<ContentControl>(pianoHost, "OHsMainWindowShellDrawerHostStyle");
        Assert.That(sampleHost.Content, Is.SameAs(samples)); Assert.That(pianoHost.Content, Is.SameAs(piano));
        var closing = shell.RequestSampleManagerCloseAsync(closePiano ? piano : samples); UiTestPump.Run(closing);
        UiTestPump.Run(closePiano ? editing : managing); view.UpdateLayout();
        Assert.That(closing.Result, Is.True);
        Assert.That((closePiano ? managing : editing).IsCompleted, Is.False);
        Assert.That((closePiano ? sampleHost : pianoHost).Content, Is.SameAs(closePiano ? (object)samples : piano));
        Assert.That(page.IsPianoRollExpanded, Is.EqualTo(expanded && !closePiano));
    }

    [TestCase(FlowDirection.LeftToRight)] [TestCase(FlowDirection.RightToLeft)]
    public void SamplesRoundTripRetainsPianoSelectionPitchAndViewportBindings(FlowDirection flow)
    {
        shell.FlowDirection = flow; page.FlowDirection = flow;
        ShowPiano(); page.TogglePianoRollExpansion(false);
        arrangement.SetNoteSelection([arrangement.Notes.Single().Id], arrangement.Notes.Single().Id);
        var roll = (PianoRollView)piano.FindName("PianoRoll");
        roll.VerticalOffset = 500;
        var selected = arrangement.SelectedNoteIds.ToArray(); var pitch = roll.VerticalOffset; var zoom = arrangement.ZoomFactor;
        var showing = ShowSamples(); Assert.That(showing.IsFaulted, Is.False);
        foreach (var width in new[] { 900d, 640d, 1200d })
        {
            shell.Width = width; view.UpdateLayout(); view.UpdateLayout();
            Assert.That(piano.TimelineViewportWidth, Is.EqualTo(page.Timeline.TimelineViewportWidth));
            Assert.That(piano.TimelineContentWidth, Is.EqualTo(page.Timeline.TimelineContentWidth));
            Assert.That(piano.TimelineHorizontalOffset, Is.EqualTo(page.Timeline.TimelineHorizontalOffset));
            Assert.That(page.IsPianoRollExpanded, Is.True);
            Assert.That(arrangement.SelectedNoteIds, Is.EqualTo(selected)); Assert.That(roll.VerticalOffset, Is.EqualTo(pitch));
        }
        UiTestPump.Run(shell.RequestSampleManagerCloseAsync(samples)); UiTestPump.Run(showing);
        Assert.That(piano.DataContext, Is.SameAs(arrangement)); Assert.That(arrangement.ZoomFactor, Is.EqualTo(zoom));
        Assert.That(arrangement.SelectedNoteIds, Is.EqualTo(selected)); Assert.That(roll.VerticalOffset, Is.EqualTo(pitch));
    }

    [Test]
    public void LateSampleTransitionCannotDetachPianoOrNewSamples()
    {
        var late = new TaskCompletionSource(); var editing = ShowPiano();
        var first = ShowSamples((_, _) => late.Task);
        Assert.That(first.IsFaulted, Is.False);
        shell.DismissSampleManager(samples); UiTestPump.Run(first);
        samples = new(manager);
        var next = ShowSamples(); late.SetException(new IOException("late transition failure"));
        UiTestPump.Run(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task);
        Assert.That(next.IsCompleted, Is.False); Assert.That(editing.IsCompleted, Is.False);
        Assert.That(((ContentControl)shell.FindName("InstrumentalDrawerHost")).Content, Is.SameAs(piano));
        Assert.That(((ContentControl)shell.FindName("SampleManagerHost")).Content, Is.SameAs(samples));
    }

    [TestCase(true)] [TestCase(false)]
    public void IndependentTransitionsAndExpansionSettleWithoutRendering(bool animations)
    {
        ShowPiano(); var showing = ShowSamples(); Assert.That(showing.IsFaulted, Is.False);
        page.TogglePianoRollExpansion(animations);
        if (animations) page.SeekPianoRollExpansion(TimeSpan.FromMilliseconds(90));
        var expansion = page.PianoRollExpansionProgress;
        var bottom = shell.RunDrawerAnimationAsync(shell.InstrumentalDrawerPresentation, true, animations, lifetime.Token);
        var side = shell.RunSampleManagerAnimationAsync(true, animations, lifetime.Token);
        Assert.That(page.PianoRollExpansionProgress, Is.EqualTo(expansion));
        if (animations)
        {
            Assert.That(side.IsCompleted, Is.False); Assert.That(bottom.IsCompleted, Is.False);
            shell.SettleSampleManagerAnimation(); UiTestPump.Run(side);
            Assert.That(bottom.IsCompleted, Is.False);
            Assert.That(page.IsPianoRollExpanded, Is.True);
            shell.SettleDrawerAnimation(shell.InstrumentalDrawerPresentation);
        }
        UiTestPump.Run(Task.WhenAll(side, bottom)); page.SettlePianoRollExpansion();
        Assert.That(page.IsPianoRollExpanded, Is.True); Assert.That(page.HasAnimatedProperties, Is.False);
    }

    [Test]
    public void DuplicateRequestsAreRejectedWithinEachSlot()
    {
        ShowPiano(); var showing = ShowSamples(); Assert.That(showing.IsFaulted, Is.False);
        var duplicatePiano = shell.PresentDrawerAsync(piano, lifetime.Token, bottomNonModal: true);
        var duplicateSamples = shell.PresentSampleManagerAsync(samples, lifetime.Token);
        Assert.That(duplicatePiano.Exception!.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(duplicateSamples.Exception!.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(((ContentControl)shell.FindName("InstrumentalDrawerHost")).Content, Is.SameAs(piano));
        Assert.That(((ContentControl)shell.FindName("SampleManagerHost")).Content, Is.SameAs(samples));
    }

    [Test]
    public void ProjectCloseSettlesBothPendingPresentations()
    {
        var entrance = new TaskCompletionSource();
        var editing = ShowPiano((_, token) => entrance.Task.WaitAsync(token));
        var managing = ShowSamples((_, token) => entrance.Task.WaitAsync(token));
        Assert.That(managing.IsFaulted, Is.False);
        var closing = shell.PrepareProjectForCloseAsync(); UiTestPump.Run(closing);
        Assert.That(closing.Result, Is.True); Assert.That(editing.IsCompletedSuccessfully, Is.True);
        Assert.That(managing.IsCompletedSuccessfully, Is.True); Assert.That(shell.IsAnyDrawerVisible, Is.False);
        Assert.That(((Grid)page.FindName("BottomDrawerSlot")).Children, Is.Empty);
        Assert.That(((Grid)page.FindName("RightDrawerSlot")).Children, Is.Empty);
        Assert.That(page.HasAnimatedProperties, Is.False);
    }
}
