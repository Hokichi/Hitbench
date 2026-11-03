using System.Windows.Controls;
using System.Windows.Data;
using Hitbench.Test.Services;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsUpdatePopupTests
{
    [Test]
    public void EscapeCancelsDownloadThroughBasePopupShortcutWithoutDisplayingWindow()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var service = new StubOHsUpdateService { Stage = async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); throw new AssertionException("Unreachable"); } };
        var vm = new OHsUpdatePopupVM(OHsUpdateTestData.Candidate(100), service);
        var popup = new OHsUpdatePopup(vm, () => { });
        using var source = new InMemoryPresentationSource();
        var downloading = vm.DownloadAsync(default);
        popup.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Escape)
            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
        UiTestPump.Run(downloading);
        Assert.That(downloading.Result, Is.Null);
        popup.Detach();
    }
    [Test]
    public void ProgressAndTextUseRealBindingsAndSharedResourceContracts()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = new OHsUpdatePopupVM(OHsUpdateTestData.Candidate(100), new StubOHsUpdateService());
        var popup = new OHsUpdatePopup(vm, () => { });
        using var view = new InMemoryView(popup, scheduleLayout: false);
        view.Root.Resources.MergedDictionaries.Add(popup.Resources);
        view.UpdateLayout();
        var progress = (ProgressBar)popup.FindName("DownloadProgress");
        var text = (TextBlock)popup.FindName("DownloadText");
        Assert.That(text.Text, Is.EqualTo("Downloading version 1.2.4-beta"));
        Assert.That(BindingOperations.GetBinding(progress, ProgressBar.ValueProperty)!.Mode, Is.EqualTo(BindingMode.OneWay));
        UiControlContract.AssertStyle<ProgressBar>(progress, "OHsDeterminateProgressStyle");
        vm.Progress = 42;
        progress.GetBindingExpression(ProgressBar.ValueProperty)!.UpdateTarget();
        Assert.That(progress.Value, Is.EqualTo(42));
        popup.Detach();
    }
}
