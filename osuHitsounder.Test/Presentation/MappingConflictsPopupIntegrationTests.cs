using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class MappingConflictsPopupIntegrationTests
{
    [TestCase(false)][TestCase(true)]
    public void CheckboxBindingsChooseOneFileAndKeyboardCompletesWithoutShowingPopup(bool cancel)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = new MappingConflictsPopupVM([new("C3", "old/C3.wav", new[] { "new/C3.wav" })]);
        bool? completed = null;
        var popup = new OHsMappingConflictsPopup(vm, value => completed = value, _ => new Size(640, 400));
        using var view = new InMemoryView(popup);
        try
        {
            Assert.That(popup.PopupTitle, Is.EqualTo("Mapping conflicts found"));
            var ok = (BalloonButton)popup.FindName("ConfirmButton");
            Assert.That(ok.Style, Is.SameAs(popup.FindResource("OHsBalloonConfirmActionStyle")));
            Assert.That(ok.ButtonIcon, Is.SameAs(popup.FindResource("Check")));
            var existing = Descendants(view.Root).OfType<CheckBox>().Single(c => c.DataContext is MappingConflictChoice { IsExisting: true });
            Assert.That(existing.Style, Is.SameAs(popup.FindResource("OHsMappingConflictChoiceStyle")));
            Assert.That(System.Windows.Automation.AutomationProperties.GetName(existing), Does.Contain("C3").And.Contain("Existing"));
            existing.SetCurrentValue(ToggleButton.IsCheckedProperty, true); view.UpdateLayout();
            Assert.That(vm.Confirm()["C3"], Is.EqualTo("old/C3.wav"));
            Assert.That(popup.HandleCompletionKey(cancel ? Key.Escape : Key.Enter), Is.True);
            Assert.That(completed, Is.EqualTo(!cancel));
            Assert.That(popup.Result is null, Is.EqualTo(cancel));
        }
        finally { popup.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
