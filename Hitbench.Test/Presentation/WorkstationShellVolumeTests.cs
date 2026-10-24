using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.ViewModels.Popups;
using Hitbench.ViewModels.Shell;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class WorkstationShellVolumeTests
{
    [SetUp]
    public void Setup()
    {
        new SongsResponsiveLayoutTests().SetUp();
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/Hitbench;component/Resources/Styles/PianoRollView.xaml", UriKind.Relative) });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AltVFromEitherDrawerOpensVolume(bool mapping) => WithDrawer(mapping, (shell, page, drawer, _) =>
    {
        Assert.That(shell.HandleWindowKeyDown(true, Key.V, ModifierKeys.Alt, false, drawer), Is.True);
        Assert.That(page.IsVolumeOverlayOpen, Is.True);
    });

    [TestCase(false)]
    [TestCase(true)]
    public void AltWheelFromEitherDrawerControlsMaster(bool mapping) => WithDrawer(mapping, (shell, page, drawer, _) =>
    {
        var vm = (WorkstationPageVM)page.DataContext;
        Assert.That(shell.HandleWindowMouseWheel(true, ModifierKeys.Alt, -120, drawer), Is.True);
        Assert.That(vm.ApplicationVolume, Is.EqualTo(100));
        Assert.That(shell.HandleWindowMouseWheel(true, ModifierKeys.Alt, -120, drawer), Is.True);
        Assert.That(vm.ApplicationVolume, Is.EqualTo(95));
        Assert.That(vm.SongVolume, Is.EqualTo(100));
        Assert.That(vm.SamplesVolume, Is.EqualTo(100));
    });

    [TestCase(false)]
    [TestCase(true)]
    public void OpenVolumeConsumesDrawerCommands(bool mapping) => WithDrawer(mapping, (shell, page, drawer, _) =>
    {
        var vm = (WorkstationPageVM)page.DataContext;
        page.ShowVolumeOverlay(false, default, true, false);
        foreach (var fading in new[] { false, true })
        {
            if (fading) page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(100), true);
            foreach (var key in new[] { Key.Space, Key.Delete, Key.F2, Key.Enter })
                Assert.That(shell.HandleWindowKeyDown(true, key, ModifierKeys.None, false, drawer), Is.True);
            Assert.That(vm.IsPlaying, Is.False);
            Assert.That(((ContentControl)shell.FindName("SampleManagerHost")).Content, Is.SameAs(drawer));
            if (drawer.DataContext is InstrumentalArrangementDrawerVM piano) Assert.That(piano.Notes, Is.Empty);
            if (drawer.DataContext is KeysMappingDrawerVM keys) Assert.That(keys.Rows.Any(row => row.IsMapped), Is.False);
        }
        Assert.That(shell.HandleWindowKeyDown(true, Key.Escape, ModifierKeys.None, false, drawer), Is.True);
        Assert.That(page.IsVolumeOverlayOpen, Is.False);
        Assert.That(shell.IsSampleManagerVisible, Is.True);
    });

    [Test]
    public void ShellVolumeDisablesUnderlyingContentOnly() => WithDrawer(false, (shell, page, drawer, _) =>
    {
        var backdrop = (Border)shell.FindName("VolumeBackdropHost");
        page.ShowVolumeOverlay(false, default, false, false);
        Assert.That(backdrop.IsEnabled, Is.False);
        Assert.That(backdrop.IsHitTestVisible, Is.False);
        Assert.That(drawer.IsEnabled, Is.False);
        Assert.That(page.ApplicationVolumeWheel.IsEnabled, Is.True);
        Assert.That(page.ApplicationVolumeWheel.IsHitTestVisible, Is.True);
        var owner = (UIElement)backdrop.Parent;
        owner.IsEnabled = false;
        page.DismissVolumeOverlay();
        Assert.That(backdrop.IsEnabled, Is.False);
        Assert.That(backdrop.IsHitTestVisible, Is.True);
        owner.ClearValue(UIElement.IsEnabledProperty);
        Assert.That(drawer.IsEnabled, Is.True);
    });

    [TestCase(false)]
    [TestCase(true)]
    public void HostedVolumeRespectsAccessibilitySettings(bool highContrast) => WithDrawer(true, (shell, page, _, _) =>
    {
        var backdrop = (Border)shell.FindName("VolumeBackdropHost");
        page.ShowVolumeOverlay(false, default, false, highContrast);
        Assert.That(backdrop.Effect is null, Is.EqualTo(highContrast));
        Assert.That(((Grid)page.FindName("WorkstationContent")).Effect, Is.Null);
        Assert.That(page.HeaderBlurEffect, Is.Null);
        Assert.That(backdrop.IsEnabled, Is.False);
        Assert.That(page.VolumeOverlay.HasAnimatedProperties, Is.False);
        page.DismissVolumeOverlay();
        Assert.That(backdrop.Effect, Is.Null);
        Assert.That(backdrop.IsEnabled, Is.True);
    });

    [Test]
    public void OpenVolumePreventsBackgroundWindowActions() => WithDrawer(false, (shell, page, _, _) =>
    {
        page.ShowVolumeOverlay(false, default, false, false);
        Assert.That(shell.HandleBackgroundMouseDown((Border)shell.FindName("ShellRoot"), 1, true,
            new Point(50, 50), new Rect(0, 0, 1280, 720), 1, 1), Is.False);
    });

    [Test]
    public void PianoViewportTracksWorkstationAfterResize() => WithDrawer(false, (shell, page, drawer, view) =>
    {
        var piano = (OHsInstrumentalArrangementDrawer)drawer;
        var roll = (Hitbench.Resources.CustomControls.PianoRollView)piano.FindName("PianoRoll");
        foreach (var width in new[] { 1280d, 900d, 640d, 1100d })
        {
            shell.Width = width;
            view.UpdateLayout();
            view.UpdateLayout();
            var expected = page.Timeline.GetTimeViewportBounds(piano);
            var actual = roll.TransformToVisual(piano).TransformBounds(new Rect(roll.KeyColumnWidth, 0, roll.TimeViewportWidth, 1));
            Assert.That(expected.IsEmpty, Is.False);
            Assert.That(actual.Left, Is.EqualTo(expected.Left).Within(.001));
            Assert.That(actual.Width, Is.EqualTo(expected.Width).Within(.001));
        }
    });

    [Test]
    public void VolumeHostDetachesOnNavigationAndClose() => WithDrawer(false, (shell, page, _, _) =>
    {
        var host = (Grid)shell.FindName("ShellVolumeOverlayHost");
        var backdrop = (Border)shell.FindName("VolumeBackdropHost");
        page.ShowVolumeOverlay(false, default, false, false);
        UiTestPump.Run(shell.ReturnHomeAsync());
        Assert.That(page.IsVolumeOverlayOpen, Is.False);
        Assert.That(host.Children, Is.Empty);
        Assert.That(backdrop.Effect, Is.Null);
        Assert.That(BindingOperations.IsDataBound(backdrop, UIElement.EffectProperty), Is.False);
        shell.Receive(new ProjectOpenRequestedMessage("project.json"));
        Assert.That(host.Children, Has.Count.EqualTo(1));
        page.ShowVolumeOverlay(false, default, false, false);
        Assert.That(backdrop.Effect, Is.Null);
        shell.Close();
        Assert.That(host.Children, Is.Empty);
    });

    private static void WithDrawer(bool mapping, Action<MainWindow, WorkstationPage, UserControl, InMemoryView> test)
    {
        var settings = new StubAppSettingsService();
        var messenger = new WeakReferenceMessenger();
        HomePage CreateHome() => new(new HomePageVM(new StubHomeFolderService(), settings,
            new StubInstallationDialogService(), new StubProjectCreationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), messenger));
        var home = CreateHome();
        var shell = TestShell.Create(home, messenger, WorkstationTestPage.Create,
            (HomePageVM)home.DataContext, settings, createHome: CreateHome);
        shell.Receive(new ProjectOpenRequestedMessage("project.json"));
        using var view = new InMemoryView(shell);
        var page = (WorkstationPage)((ContentControl)shell.FindName("PageHost")).Content;
        UiTestPump.Run(page.LoadCompletion);
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new InstrumentalTrackSnapshot(context, "Piano", 100, new Dictionary<string, string>(), [], null, 0);
        UserControl drawer = mapping
            ? new OHsKeysMappingDrawer(new KeysMappingDrawerVM(snapshot, messenger, new ImmediateUiScheduler(),
                new StubSampleFileService([]), new StubSampleFileDialogService((string?)null)))
            : new OHsInstrumentalArrangementDrawer(new InstrumentalArrangementDrawerVM(
                InstrumentalArrangementTestContext.Create(snapshot, [0], [120], TimeSpan.Zero, 4, 32), messenger, new ImmediateUiScheduler()));
        using var cancellation = new CancellationTokenSource();
        var presentation = drawer is OHsKeysMappingDrawer keys
            ? shell.PresentDrawerAsync(keys, cancellation.Token, (_, _) => Task.CompletedTask, true)
            : shell.PresentDrawerAsync((OHsInstrumentalArrangementDrawer)drawer, cancellation.Token, (_, _) => Task.CompletedTask, true);
        // InMemoryView deliberately detaches the root from Window; supply the open-layer lifecycle explicitly.
        ((Grid)shell.FindName("SampleManagerLayer")).Visibility = Visibility.Visible;
        view.UpdateLayout();
        try { test(shell, page, drawer, view); }
        finally
        {
            cancellation.Cancel();
            UiTestPump.Run(presentation);
            if (drawer.DataContext is KeysMappingDrawerVM keyVM) UiTestPump.Run(keyVM.DeactivateAsync());
            if (drawer.DataContext is InstrumentalArrangementDrawerVM pianoVM) UiTestPump.Run(pianoVM.DeactivateAsync());
            shell.Close();
        }
    }
}
