using System.Windows;
using System.Windows.Controls;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsSampleDropImportPopupIntegrationTests
{
    [Test]
    public void RealCheckboxBindingAndActionsRespectSelectionAndTabSuppression()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts(); var vm = new OHsSampleDropImportPopupVM(new[] { "C:/a.wav" }, new StubSampleBrowserService(), new StubSamplePreviewService(), new ImmediateUiScheduler()); UiTestPump.Run(vm.InitializeAsync(default)); bool? accepted = null; var popup = new OHsSampleDropImportPopup(vm, value => accepted = value) { Width = 700, Height = 500 }; KeyboardNavigationPolicy.SetSuppressTabNavigation(popup, true);
        using var view = new InMemoryView(popup, scheduleLayout: false); view.UpdateLayout();
        try { var list = (ListBox)popup.FindName("FileList"); var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0); var checkbox = Children(container).OfType<CheckBox>().Single(); Assert.That(checkbox.Style.BasedOn, Is.SameAs(popup.FindResource("RegularCheckBoxStyle"))); Assert.That(System.Windows.Input.KeyboardNavigation.GetIsTabStop(checkbox), Is.False); checkbox.IsChecked = false; Assert.That(vm.CanConfirm, Is.False); Assert.That(((BalloonButton)popup.FindName("ImportAction")).IsEnabled, Is.False); popup.HandleKey(System.Windows.Input.Key.Enter); Assert.That(accepted, Is.Null); checkbox.IsChecked = true; popup.HandleKey(System.Windows.Input.Key.Enter); Assert.That(accepted, Is.True); popup.HandleKey(System.Windows.Input.Key.Escape); Assert.That(accepted, Is.False); }
        finally { popup.ReleaseOwner(); UiTestPump.Run(vm.DeactivateAsync()); popup.Close(); }
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent) { yield return parent; for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++) foreach (var child in Children(System.Windows.Media.VisualTreeHelper.GetChild(parent, i))) yield return child; }
}
