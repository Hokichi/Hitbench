using System.Windows;
using System.Windows.Controls;
using Hitbench.Resources.DataModels;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class HomeMapsetScanningIntegrationTests
{
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
