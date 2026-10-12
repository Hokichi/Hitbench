using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;
using osuHitsounder.Views.Pages;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using System.Windows.Input;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)] [NonParallelizable]
public sealed class ManageSamplesDrawerAnimationTests
{
    private ServiceProvider services = null!;
    private MainWindow shell = null!;
    private ManageSamplesPopupVM manager = null!;
    private ManageSamplesDrawer drawer = null!;
    private CancellationTokenSource lifetime = null!;
    private SynchronizationContext? previousContext;
    private readonly List<Task> presentations = [];

    [SetUp]
    public void SetUp()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        services = App.CreateServices(); shell = services.GetRequiredService<MainWindow>();
        previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(shell.Dispatcher));
        var files = new StubSampleFileService(["hit.wav"]);
        manager = new(new("project.json", null, Guid.NewGuid()), files, new(files), new StubSampleFileDialogService([]),
            new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new osuHitsounder.Services.Ui.UiScheduler(shell.Dispatcher));
        UiTestPump.Run(manager.InitializeAsync(default)); drawer = new(manager); lifetime = new();
        presentations.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            lifetime.Cancel();
            if (((ContentControl)shell.FindName("SampleManagerHost")).Content is ManageSamplesDrawer active) shell.DismissSampleManager(active);
            UiTestPump.Run(Task.WhenAll(presentations)); UiTestPump.Run(manager.DeactivateAsync()); shell.Close(); services.Dispose(); lifetime.Dispose();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private Task Show(ManageSamplesDrawer target, Func<bool, CancellationToken, Task> transition)
    {
        var showing = shell.PresentSampleManagerAsync(target, lifetime.Token, transition); presentations.Add(showing); Drain(); return showing;
    }

    private static void Drain() => UiTestPump.Run(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task);

    [Test]
    public void CoveredWorkstationDoesNotConsumeDrawerKeyboardOrWheelInput()
    {
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler());
        var page = new WorkstationPage(vm);
        try
        {
            UiTestPump.Run(vm.LoadAsync("project.json", default)); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            ((ContentControl)shell.FindName("PageHost")).Content = page;
            Show(drawer, (_, _) => Task.CompletedTask);
            Assert.That(vm.PlayPauseCommand.CanExecute(null), Is.True);
            Assert.That(shell.HandleWindowKeyDown(true, Key.Space, ModifierKeys.None, false, drawer), Is.False);
            Assert.That(shell.HandleWindowKeyDown(true, Key.Left, ModifierKeys.None, false, drawer), Is.False);
            Assert.That(shell.HandleWindowKeyDown(true, Key.V, ModifierKeys.Alt, false, drawer), Is.False);
            Assert.That(shell.HandleWindowMouseWheel(true, ModifierKeys.Control, 120, page), Is.False);
            Assert.That(audio.Snapshot.IsPlaying, Is.False);
        }
        finally { UiTestPump.Run(page.PlaybackInputCompletion); UiTestPump.Run(page.DeactivateAsync()); }
    }

    [Test]
    public void ReducedMotionCompletesSynchronously()
    {
        Assert.That(shell.RunSampleManagerAnimationAsync(true, false, default).IsCompletedSuccessfully, Is.True);
        Assert.That(shell.RunSampleManagerAnimationAsync(false, false, default).IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public void SettingChangeSettlesPendingAnimation()
    {
        var animation = shell.RunSampleManagerAnimationAsync(true, true, default);
        Assert.That(animation.IsCompleted, Is.False);
        shell.SettleSampleManagerAnimation(); UiTestPump.Run(animation);
        Assert.That(animation.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public void RealAnimationCancellationNeedsNoRenderTick()
    {
        using var cancellation = new CancellationTokenSource();
        var animation = shell.RunSampleManagerAnimationAsync(true, true, cancellation.Token);
        Assert.That(animation.IsCompleted, Is.False); cancellation.Cancel();
        UiTestPump.Run(animation.ContinueWith(_ => { }, TaskScheduler.Default));
        Assert.That(animation.IsCanceled, Is.True);
        Assert.That(shell.RunSampleManagerAnimationAsync(false, false, default).IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public void CloseKeepsBackdropUntilExitCompletes()
    {
        var exited = new TaskCompletionSource();
        var showing = Show(drawer, (opening, token) => opening ? Task.CompletedTask : exited.Task.WaitAsync(token));
        var closing = shell.RequestSampleManagerCloseAsync(drawer); Drain();
        Assert.That(shell.SampleManagerPhase, Is.EqualTo(OHsSampleDrawerPhase.Closing));
        Assert.That(shell.IsSampleManagerVisible, Is.True); Assert.That(showing.IsCompleted, Is.False); Assert.That(closing.IsCompleted, Is.False);
        Assert.That(((ContentControl)shell.FindName("SampleManagerHost")).Content, Is.SameAs(drawer)); Assert.That(drawer.IsEnabled, Is.False);
        exited.SetResult(); UiTestPump.Run(closing); UiTestPump.Run(showing);
        Assert.That(shell.SampleManagerPhase, Is.EqualTo(OHsSampleDrawerPhase.Closed)); Assert.That(shell.IsSampleManagerVisible, Is.False);
    }

    [Test]
    public void CloseDuringEntranceStartsOneExit()
    {
        var entered = new TaskCompletionSource(); var exited = new TaskCompletionSource(); var exitStarted = new TaskCompletionSource(); var exitCalls = 0;
        var showing = Show(drawer, (opening, token) => opening ? entered.Task.WaitAsync(token) : Exit(token));
        Task Exit(CancellationToken token) { exitCalls++; exitStarted.TrySetResult(); return exited.Task.WaitAsync(token); }
        Assert.That(shell.SampleManagerPhase, Is.EqualTo(OHsSampleDrawerPhase.Opening));
        var closing = shell.RequestSampleManagerCloseAsync(drawer); var repeated = shell.RequestSampleManagerCloseAsync(drawer); Drain();
        UiTestPump.Run(exitStarted.Task);
        Assert.That(repeated, Is.SameAs(closing)); Assert.That(exitCalls, Is.EqualTo(1));
        entered.SetResult(); Drain(); Assert.That(shell.SampleManagerPhase, Is.EqualTo(OHsSampleDrawerPhase.Closing));
        exited.SetResult(); UiTestPump.Run(closing); UiTestPump.Run(showing);
    }

    [Test]
    public void RepeatedDismissalCannotOpenAnotherDrawerDuringExit()
    {
        var exited = new TaskCompletionSource(); var exits = 0;
        var showing = Show(drawer, (opening, token) => { if (opening) return Task.CompletedTask; exits++; return exited.Task.WaitAsync(token); });
        var closing = shell.RequestSampleManagerCloseAsync(drawer); Drain();
        var duplicate = shell.PresentSampleManagerAsync(new ManageSamplesDrawer(manager), lifetime.Token, (_, _) => Task.CompletedTask);
        if (!duplicate.IsFaulted) presentations.Add(duplicate);
        Assert.That(duplicate.IsFaulted, Is.True);
        Assert.That(duplicate.Exception!.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(shell.RequestSampleManagerCloseAsync(drawer), Is.SameAs(closing)); Assert.That(exits, Is.EqualTo(1));
        exited.SetResult(); UiTestPump.Run(closing); UiTestPump.Run(showing);
    }

    [TestCase(true)][TestCase(false)]
    public void CancellationDuringEntranceOrExitSettlesWithoutRendering(bool entrance)
    {
        var transition = new TaskCompletionSource();
        var showing = Show(drawer, (opening, token) => opening != entrance ? Task.CompletedTask : transition.Task.WaitAsync(token));
        Task closing = Task.CompletedTask;
        if (entrance) { UiTestPump.Run(manager.EditCommand.ExecuteAsync(manager.Entries.Single())); manager.DraftStem = "kick"; }
        else { closing = shell.RequestSampleManagerCloseAsync(drawer); Drain(); }
        lifetime.Cancel(); UiTestPump.Run(showing); UiTestPump.Run(closing);
        Assert.That(shell.SampleManagerPhase, Is.EqualTo(OHsSampleDrawerPhase.Closed)); Assert.That(shell.IsSampleManagerVisible, Is.False);
        Assert.That(((ContentControl)shell.FindName("SampleManagerHost")).Content, Is.Null);
        Assert.That(manager.Entries.Single().FileName, Is.EqualTo("hit.wav"));
    }

    [Test]
    public void LateCompletionCannotCloseNewPresentation()
    {
        var late = new TaskCompletionSource(); var first = Show(drawer, (_, _) => late.Task);
        shell.DismissSampleManager(drawer); UiTestPump.Run(first);
        var nextDrawer = new ManageSamplesDrawer(manager); var next = Show(nextDrawer, (_, _) => Task.CompletedTask);
        late.SetResult(); Drain();
        Assert.That(next.IsCompleted, Is.False); Assert.That(shell.SampleManagerPhase, Is.EqualTo(OHsSampleDrawerPhase.Open));
        Assert.That(((ContentControl)shell.FindName("SampleManagerHost")).Content, Is.SameAs(nextDrawer));
        shell.DismissSampleManager(nextDrawer); UiTestPump.Run(next);
    }

    [Test]
    public void NestedModalRoundTripDuringDrawerPresentationPreservesBackdrop()
    {
        var showing = Show(drawer, (_, _) => Task.CompletedTask);
        shell.ShowPopupOverlay(); shell.HidePopupOverlay();
        Assert.That(shell.IsPopupOverlayVisible, Is.False); Assert.That(shell.IsSampleManagerVisible, Is.True);
        Assert.That(shell.SampleManagerPhase, Is.EqualTo(OHsSampleDrawerPhase.Open)); Assert.That(showing.IsCompleted, Is.False);
    }
}
