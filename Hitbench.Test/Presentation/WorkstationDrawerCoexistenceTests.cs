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
