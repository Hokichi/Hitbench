using CommunityToolkit.Mvvm.Messaging;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;
namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsExportPopupIntegrationTests
{
    [Test]
    public void AssignedControlsBindOptionsValidateAndConfirmWithoutShowingWindow()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = new OHsExportPopupVM(new(Guid.NewGuid(), "project.json", "map", "audio", "{}", new("Hitsounds", 1, ".ogg", ".ogg", false)), new StubOHsExportService(), new StubOHsExportDirectoryLauncher(), new WeakReferenceMessenger());
        bool? completion = null; var popup = new OHsExportPopup(vm, accepted => completion = accepted);
        using var source = new InMemoryPresentationSource(); using var view = new InMemoryView(popup, scheduleLayout: false);
        view.Root.Resources.MergedDictionaries.Add(popup.Resources);
        try
        {
            var name = (TextBox)popup.FindName("DiffNameInput");
            var index = (NumericUpDown)popup.FindName("StartingIndexInput");
            var single = (ComboBox)popup.FindName("SingleFormatInput"); var mixed = (ComboBox)popup.FindName("MixedFormatInput");
            var show = (CheckBox)popup.FindName("ShowFilesAfterExportInput"); var export = (BalloonButton)popup.FindName("ExportButton");
            var destination = (SegmentedToggleGroup)popup.FindName("DestinationInput");
            var overwrite = (CheckBox)popup.FindName("OverwriteEverythingInput");
            UiControlContract.AssertStyle<SegmentedToggleGroup>(destination,"OHsExportDestinationGroupStyle");
            UiControlContract.AssertStyle<CheckBox>(overwrite,"OHsExportShowFilesStyle");
            destination.SelectedValue=OHsExportDestination.Beatmap; view.UpdateLayout();
            Assert.That(vm.Destination,Is.EqualTo(OHsExportDestination.Beatmap));
            Assert.That(overwrite.IsEnabled,Is.False);
            destination.SelectedValue=OHsExportDestination.Project; view.UpdateLayout();
            Assert.That(overwrite.IsEnabled,Is.True);
            overwrite.IsChecked=true; Assert.That(vm.OverwriteEverything,Is.True);
            var progress = (ProgressBar)popup.FindName("ExportProgressBar");
            UiControlContract.AssertStyle<TextBox>(name, "OHsExportNameInputStyle"); UiControlContract.AssertStyle<NumericUpDown>(index, "OHsExportIndexInputStyle");
            UiControlContract.AssertStyle<ComboBox>(single, "OHsExportFormatInputStyle"); UiControlContract.AssertStyle<ComboBox>(mixed, "OHsExportFormatInputStyle");
            UiControlContract.AssertStyle<CheckBox>(show, "OHsExportShowFilesStyle"); UiControlContract.AssertStyle<BalloonButton>(export, "OHsBalloonConfirmActionStyle");
            UiControlContract.AssertStyle<ProgressBar>(progress, "OHsExportProgressStyle");
            Assert.That(AutomationProperties.GetName(progress), Is.EqualTo("Export progress"));
            vm.IsBusy = true; vm.ExportProgress = 42; view.UpdateLayout();
            Assert.That(progress.Value, Is.EqualTo(42));
            Assert.That(progress.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(show.IsEnabled, Is.False);
            vm.IsBusy = false; view.UpdateLayout();
            Assert.That(progress.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(export.ButtonIcon, Is.SameAs(popup.FindResource("Export")));
            foreach (var control in new FrameworkElement[] { name, index, single, mixed, show, export }) Assert.That(AutomationProperties.GetName(control), Is.Not.Empty);
            name.Text = "../bad"; name.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); popup.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Assert.That(Validation.GetHasError(name), Is.True); Assert.That(vm.CanExport, Is.False);
            name.Text = "Custom"; name.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            single.SelectedItem = ".wav"; mixed.SelectedItem = ".mp3"; show.IsChecked = true;
            index.Value = 999; popup.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Assert.That((vm.DiffName, vm.StartingIndex, vm.SingleFormat, vm.MixedFormat, vm.ShowFilesAfterExport), Is.EqualTo(("Custom", 999, ".wav", ".mp3", true)));
            var editor = (TextBox)index.Template.FindName("PART_TextBox", index);
            Assert.That(editor.IsTabStop, Is.True); editor.Text = "";
            index.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.That(index.HasInputError, Is.True); Assert.That(vm.CanExport, Is.False);
            editor.Text = "999"; index.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.That(vm.CanExport, Is.True);
            popup.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.That(completion, Is.True);
        }
        finally { popup.Detach(); UiTestPump.Run(vm.DeactivateAsync()); }
    }
}
