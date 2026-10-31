using System.Windows;
using System.Windows.Controls;
using Hitbench.Resources.DataModels;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class HomeMapsetScanningIntegrationTests
{
    [Test]
    public void PreparingListRejectsInputAndVisibilityLossSuspendsReadiness()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var pending = new TaskCompletionSource<FolderSnapshot?>();
        var vm = HomeMapsetScanningTests.Create(new StubHomeFolderService { Songs = (_, _) => pending.Task });
        var page = new HomePage(vm);
        using var view = new InMemoryView(new Window { Content = page, Width = 1200, Height = 800 });
        var list = (ListBox)page.FindName("SongsList");
        try
        {
            var loading = vm.LoadAsync(default);
            Assert.That(list.IsHitTestVisible, Is.False);
            Assert.That(list.IsEnabled, Is.False);
            Assert.That(ToolTipService.GetIsEnabled(list), Is.False);
            pending.SetResult(new FolderSnapshot([new FolderEntry("one", "one", default)]));
            UiTestPump.Run(loading);
            UiTestPump.Run(page.LoadSongsPresentationAsync(default));
            view.UpdateLayout();
            Assert.That(list.IsHitTestVisible, Is.True);
            Assert.That(list.IsEnabled, Is.True);
            page.Visibility = Visibility.Hidden;
            Assert.That(list.IsHitTestVisible, Is.False);
            Assert.That(vm.CanSelectMapset, Is.False);
            page.Visibility = Visibility.Visible;
            UiTestPump.Run(page.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
        }
        finally { pending.TrySetResult(new FolderSnapshot([])); page.Deactivate(); }
    }
    [TestCase("LoadingSpinnerD.json")] [TestCase("LoadingSpinnerL.json")]
    public void BothSpinnerResourcesParseWithoutDisplaying(string fileName)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var player = new Hitbench.Resources.CustomControls.OHsScanningAnimationView { AutoPlay = false };
        Assert.DoesNotThrow(() => player.ResourcePath = "pack://application:,,,/Hitbench;component/Resources/" + fileName);
        Assert.That(player.IsPlaying, Is.False);
    }

    [TestCase(0)] [TestCase(1)]
    public void InitialLiveAndRolledBackThemeSelectCorrectSpinner(int initialTheme)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var settings = new StubAppSettingsService();
        var service = new Hitbench.Services.Ui.OHsThemeService(new ResourceDictionary(), settings, new ImmediateUiScheduler());
        service.ApplyInitialTheme(initialTheme);
        var pending = new TaskCompletionSource<FolderSnapshot?>();
        var vm = HomeMapsetScanningTests.Create(new StubHomeFolderService { Songs = (_, token) => pending.Task.WaitAsync(token) });
        var page = new HomePage(vm, service);
        Task loading = Task.CompletedTask;
        using var view = new InMemoryView(new Window { Content = page, Width = 1200, Height = 800 });
        string Expected(int theme) => theme == 1 ? "LoadingSpinnerL.json" : "LoadingSpinnerD.json";
        try
        {
            Assert.That(page.ScanningResourcePath, Does.EndWith(Expected(initialTheme)));
            Assert.That(page.IsScanningAnimationPlaying, Is.False);
            loading = vm.LoadAsync(default);
            page.RefreshScanningPresentation(true, false);
            UiTestPump.Run(service.ChangeThemeAsync(1 - initialTheme, default));
            Assert.That(page.ScanningResourcePath, Does.EndWith(Expected(1 - initialTheme)));
            Assert.That(page.IsScanningAnimationPlaying, Is.True);
            page.RefreshScanningPresentation(false, false);
            settings.SaveTheme = (_, _) => throw new IOException("theme save failed");
            Assert.Throws<IOException>(() => UiTestPump.Run(service.ChangeThemeAsync(initialTheme, default)));
            Assert.That(page.ScanningResourcePath, Does.EndWith(Expected(1 - initialTheme)));
            Assert.That(page.IsScanningAnimationPlaying, Is.False);
            page.Deactivate();
            service.ApplyInitialTheme(initialTheme);
            Assert.That(page.ScanningResourcePath, Does.EndWith(Expected(1 - initialTheme)));
            Assert.That(page.IsScanningAnimationPlaying, Is.False);
        }
        finally { page.Deactivate(); try { UiTestPump.Run(loading); } catch (OperationCanceledException) { } }
    }

    [TestCase("idle")] [TestCase("hidden")] [TestCase("reduced motion")] [TestCase("high contrast")]
    public void ThemeSwitchPreservesPlaybackPolicy(string policy)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var service = new Hitbench.Services.Ui.OHsThemeService(new ResourceDictionary(), new StubAppSettingsService(), new ImmediateUiScheduler());
        service.ApplyInitialTheme(0);
        var pending = new TaskCompletionSource<FolderSnapshot?>();
        var vm = HomeMapsetScanningTests.Create(new StubHomeFolderService { Songs = (_, token) => pending.Task.WaitAsync(token) });
        var page = new HomePage(vm, service);
        Task loading = Task.CompletedTask;
        using var view = new InMemoryView(new Window { Content = page, Width = 1200, Height = 800 });
        try
        {
            if (policy != "idle") loading = vm.LoadAsync(default);
            if (policy == "hidden") page.Visibility = Visibility.Hidden;
            page.RefreshScanningPresentation(policy != "reduced motion", policy == "high contrast");
            UiTestPump.Run(service.ChangeThemeAsync(1, default));
            Assert.That(page.ScanningResourcePath, Does.EndWith("LoadingSpinnerL.json"));
            Assert.That(page.IsScanningAnimationPlaying, Is.False);
            Assert.That(vm.IsScanningMapsets, Is.EqualTo(policy != "idle"));
        }
        finally { page.Deactivate(); try { UiTestPump.Run(loading); } catch (OperationCanceledException) { } }
    }

    [Test]
    public void ScanStatusSurvivesReducedMotionAndAnimationFailure()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var pending = new TaskCompletionSource<FolderSnapshot?>();
        var vm = HomeMapsetScanningTests.Create(new StubHomeFolderService { Songs = (_, token) => pending.Task.WaitAsync(token) });
        Task loading = Task.CompletedTask;
        var page = new HomePage(vm);
        try
        {
            using var view = new InMemoryView(new Window { Content = page, Width = 1200, Height = 800 });
            loading = vm.LoadAsync(default);
            page.RefreshScanningPresentation(false, false);
            var text = (TextBlock)page.FindName("MapsetScanningText");
            Assert.That(text.Text, Is.EqualTo("Scanning..."));
            Assert.That(text.IsVisible, Is.True);
            UiControlContract.AssertStyle<TextBlock>(text, "OHsHomeScanningTextStyle");
            page.LoadScanningAnimation("pack://application:,,,/Hitbench;component/Resources/missing.json");
            page.RefreshScanningPresentation(true, false);
            Assert.That(text.IsVisible, Is.True);
            pending.SetResult(new FolderSnapshot([])); UiTestPump.Run(loading);
            view.UpdateLayout();
            Assert.That(text.IsVisible, Is.False);
        }
        finally { page.Deactivate(); try { UiTestPump.Run(loading); } catch (OperationCanceledException) { } }
    }

    [Test]
    public void MotionPolicyStopsAndRestartsOnlyActiveAttachedScan()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var pending = new TaskCompletionSource<FolderSnapshot?>();
        var vm = HomeMapsetScanningTests.Create(new StubHomeFolderService { Songs = (_, token) => pending.Task.WaitAsync(token) });
        Task loading = Task.CompletedTask;
        var page = new HomePage(vm);
        try
        {
            using var view = new InMemoryView(new Window { Content = page, Width = 1200, Height = 800 });
            loading = vm.LoadAsync(default);
            page.RefreshScanningPresentation(true, false);
            Assert.That(page.IsScanningAnimationPlaying, Is.True);
            page.RefreshScanningPresentation(false, false);
            Assert.That(page.IsScanningAnimationPlaying, Is.False);
            page.RefreshScanningPresentation(true, true);
            Assert.That(page.IsScanningAnimationPlaying, Is.False);
            page.RefreshScanningPresentation(true, false);
            Assert.That(page.IsScanningAnimationPlaying, Is.True);
            page.Visibility = Visibility.Hidden;
            page.RefreshScanningPresentation(true, false);
            Assert.That(page.IsScanningAnimationPlaying, Is.False);
            page.Visibility = Visibility.Visible;
            page.Deactivate();
            page.RefreshScanningPresentation(true, false);
            Assert.That(page.IsScanningAnimationPlaying, Is.False);
        }
        finally { page.Deactivate(); try { UiTestPump.Run(loading); } catch (OperationCanceledException) { } }
    }
}
