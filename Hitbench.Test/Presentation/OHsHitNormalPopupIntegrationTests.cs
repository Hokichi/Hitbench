using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsHitNormalPopupIntegrationTests
{
    [Test]
    public void KeyboardTraversesEveryCatalogChoiceAsCurrentRowsMoveToTop()
    {
        var vm = new OHsHitNormalPopupVM(new(), [new("a.wav", "a.wav"), new("b.wav", "b.wav"), new("c.wav", "c.wav")]);
        WithPopup(vm, (popup, view, _) =>
        {
            var list = (ListBox)popup.FindName("SampleList");
            foreach (var path in new[] { "a.wav", "b.wav", "c.wav" })
            {
                Assert.That(popup.HandleKey(Key.Down, list), Is.True);
                Assert.That(vm.NormalPath, Is.EqualTo(path));
            }
            popup.HandleKey(Key.Up, list);
            Assert.That(vm.NormalPath, Is.EqualTo("b.wav"));
            popup.HandleKey(Key.Home, list);
            Assert.That(vm.NormalPath, Is.Empty);
            popup.HandleKey(Key.End, list);
            Assert.That(vm.NormalPath, Is.EqualTo("c.wav"));
            vm.Groups[3].IsExpanded = false;
            popup.HandleKey(Key.Home, list);
            popup.HandleKey(Key.Down, list);
            Assert.That(vm.NormalPath, Is.Empty);
        });
    }

    [TestCase(false)] [TestCase(true)]
    public void RealSelectionsAssignOnceWithoutPlayAndKeyboardCompletesDraft(bool save)
    {
        var vm = new OHsHitNormalPopupVM(new(), [new("a.wav", "Samples/a.wav"),
            new("SkinDefault/normal-hitnormal.wav", "normal-hitnormal.wav", OHsHitNormalSampleSource.Skin)]);
        WithPopup(vm, (popup, view, result) =>
        {
            var slots = (ListBox)popup.FindName("SlotList");
            var samples = (ListBox)popup.FindName("SampleList");
            UiControlContract.AssertStyle<ListBox>(slots, "OHsHitNormalSlotListStyle");
            UiControlContract.AssertStyle<ListBox>(samples, "OHsHitNormalSampleListStyle");
            Assert.That(samples.FocusVisualStyle, Is.SameAs(samples.FindResource("DefaultFocusVisualStyle")));
            Assert.That(slots.FocusVisualStyle, Is.SameAs(slots.FindResource("DefaultFocusVisualStyle")));
            Assert.That(AutomationProperties.GetName(slots), Is.EqualTo("Hitnormal slots"));
            Assert.That(AutomationProperties.GetName(samples), Is.EqualTo("Assign hitnormal sample"));
            Assert.That(popup.NextFocusTarget(samples, false), Is.SameAs(popup.FindName("CancelAction")));
            Assert.That(popup.NextFocusTarget(null, true), Is.SameAs(popup.FindName("SaveAction")));
            var assignments = 0;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName is "NormalPath" or "SoftPath" or "DrumPath") assignments++; };
            var custom = vm.CatalogSamples.Single(s => s.Path == "a.wav");
            foreach (var slot in vm.Slots)
            {
                slots.SelectedItem = slot;
                samples.SelectedItem = custom;
                view.UpdateLayout();
                Assert.That(vm.SelectedSlot, Is.SameAs(slot));
                Assert.That(vm.PinnedSamples.Last(), Is.SameAs(custom));
            }
            Assert.That(assignments, Is.EqualTo(3));
            Assert.That(vm.CreateResult(), Is.EqualTo(new OHsHitNormalConfiguration("a.wav", "a.wav", "a.wav")));
            Assert.That(Children(view.Root).OfType<OHsSampleRow>().All(r => !r.ShowPlayButton), Is.True);
            Assert.That(Children(view.Root).OfType<Control>().All(c => !c.IsTabStop), Is.True);
            Assert.That(popup.HandleKey(Key.Enter, samples), Is.True);
            Assert.That(result(), Is.Null, "Enter on a catalog row must not save the dialog.");
            if (save) popup.HandleShortcut(Key.V, ModifierKeys.Alt);
            else popup.HandleKey(Key.Escape);
            Assert.That(result(), Is.EqualTo(save));
            Assert.That(popup.TryComplete(!save), Is.False);
            Assert.That(popup.IsVisible, Is.False);
            UiControlContract.AssertStyle<BalloonButton>((BalloonButton)popup.FindName("SaveAction"), "OHsBalloonConfirmActionStyle");
        });
    }

    [TestCase("Dark", FlowDirection.LeftToRight)] [TestCase("Light", FlowDirection.RightToLeft)]
    public void DefaultsPillsGroupingAndKeyboardRemainAvailableWithEmptyCollapsedSections(string theme, FlowDirection flow)
    {
        var vm = new OHsHitNormalPopupVM(new("a.wav"), [new("a.wav", "a.wav")]);
        WithPopup(vm, (popup, view, result) =>
        {
            var list = (ListBox)popup.FindName("SampleList");
            Assert.That(list.Items.Groups, Has.Count.EqualTo(4));
            var currentRow = Children(list).OfType<OHsSampleRow>().Single(r => ReferenceEquals(r.DataContext, vm.PinnedSamples.Last()));
            UiControlContract.AssertStyle<OHsSampleRow>(currentRow, "OHsHitNormalSampleRowStyle");
            foreach (var slotRow in Children((ListBox)popup.FindName("SlotList")).OfType<OHsSampleRow>())
                UiControlContract.AssertStyle<OHsSampleRow>(slotRow, "OHsHitNormalSampleRowStyle");
            Assert.That(currentRow.NameTrailingTemplate, Is.SameAs(popup.FindResource("OHsHitNormalPillsTemplate")));
            Assert.That(Children(list).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text), Does.Contain("default").And.Contain("current"));
            list.SelectedItem = vm.PinnedSamples[0];
            view.UpdateLayout();
            Assert.That(vm.NormalPath, Is.Empty);
            Assert.That(vm.PinnedSamples, Has.Count.EqualTo(1));
            Assert.That(vm.PinnedSamples[0].IsDefault && vm.PinnedSamples[0].IsCurrent, Is.True);
            var mergedRow = Children(list).OfType<OHsSampleRow>().Single(r => ReferenceEquals(r.DataContext, vm.PinnedSamples[0]));
            Assert.That(Children(mergedRow).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text),
                Does.Contain("default").And.Contain("current"));
            Assert.That(popup.HandleShortcut(Key.D2, ModifierKeys.Alt), Is.True);
            Assert.That(vm.Groups[2].IsExpanded, Is.False);
            Assert.That(popup.HandleShortcut(Key.S, ModifierKeys.Alt), Is.True);
            Assert.That(vm.SelectedSlot.Name, Is.EqualTo("Soft"));
            Assert.That(vm.PinnedSamples.Single().DisplayName, Is.EqualTo("soft-hitnormal"));
            Assert.That(list.FlowDirection, Is.EqualTo(flow));
            Assert.That(result(), Is.Null);
            vm.NormalPath = "../bad.wav";
            Assert.That(((BalloonButton)popup.FindName("SaveAction")).IsEnabled, Is.False);
            Assert.That(popup.TryComplete(true), Is.False);
        }, theme, flow);
    }

    [TestCase(FlowDirection.LeftToRight)] [TestCase(FlowDirection.RightToLeft)]
    public void LargeGroupedCatalogRecyclesAndStillAssignsInExplicitSmallWorkArea(FlowDirection direction)
    {
        var vm = new OHsHitNormalPopupVM(new(), Enumerable.Range(0, 3000)
            .Select(i => new OHsHitNormalChoice($"{i}.wav", $"Samples/{i}.wav")).ToArray());
        WithPopup(vm, (popup, view, _) =>
        {
            popup.ApplyWorkArea(new Size(320, 240), 96);
            view.UpdateLayout();
            var list = (ListBox)popup.FindName("SampleList");
            Assert.That(list.Items.Count, Is.EqualTo(3001));
            Assert.That(list.ItemsPanel, Is.SameAs(list.FindResource("OHsVirtualizedDrawerItemsPanel")));
            Assert.That(VirtualizingPanel.GetIsVirtualizingWhenGrouping(list), Is.True);
            Assert.That(VirtualizingPanel.GetVirtualizationMode(list), Is.EqualTo(VirtualizationMode.Recycling));
            Assert.That(Children(list).OfType<ListBoxItem>().Count(), Is.LessThan(list.Items.Count));
            var target = vm.CatalogSamples.Single(s => s.Path == "2999.wav");
            list.ScrollIntoView(target);
            view.UpdateLayout();
            Assert.That(Children(list).OfType<OHsSampleRow>().Any(r => ReferenceEquals(r.DataContext, target)), Is.True,
                "Scrolling must realize the requested catalog row.");
            list.SelectedItem = target;
            Assert.That(vm.NormalPath, Is.EqualTo("2999.wav"));
            Assert.That(vm.PinnedSamples.Last(), Is.SameAs(target));
            Assert.That(((BalloonButton)popup.FindName("SaveAction")).IsEnabled, Is.True);
            Assert.That(((BalloonButton)popup.FindName("CancelAction")).IsEnabled, Is.True);
        }, flow: direction);
    }

    private static void WithPopup(OHsHitNormalPopupVM vm, Action<OHsHitNormalPopup, InMemoryView, Func<bool?>> check,
        string theme = "Dark", FlowDirection flow = FlowDirection.LeftToRight)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/Hitbench;component/Resources/Styles/GroupItem.xaml", UriKind.Relative) });
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
            { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        UiTestPump.Run(vm.InitializeAsync(default));
        bool? accepted = null;
        var popup = new OHsHitNormalPopup(vm, value => accepted = value) { FlowDirection = flow };
        ((FrameworkElement)popup.Content).Resources.MergedDictionaries.Add(popup.Resources);
        try
        {
            using var view = new InMemoryView(popup, scheduleLayout: false);
            view.UpdateLayout();
            check(popup, view, () => accepted);
        }
        finally { popup.ReleaseOwner(); UiTestPump.Run(vm.DeactivateAsync()); popup.Close(); }
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
