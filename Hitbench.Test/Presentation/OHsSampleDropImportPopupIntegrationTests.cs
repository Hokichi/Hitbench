using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsSampleDropImportPopupIntegrationTests
{
    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void NavigationDoesNotChangeCheckedSelectionOrCompleteImport(FlowDirection flow)
    {
        var preview = new StubSamplePreviewService();
        var vm = new OHsSampleDropImportPopupVM(["C:/a.wav", "C:/b.wav", "C:/c.wav"], new StubSampleBrowserService(), preview, new ImmediateUiScheduler());
        WithPopup(vm, (popup, view) =>
        {
            var list = (ListBox)popup.FindName("FileList");
            vm.Entries[0].IsChecked = false;
            list.SelectedIndex = 0;
            foreach (var (key, expected) in new[] { (Key.Down, 1), (Key.End, 2), (Key.Up, 1), (Key.Home, 0) })
            {
                Assert.That(popup.HandleKey(key), Is.True);
                Assert.That(list.SelectedIndex, Is.EqualTo(expected));
                Assert.That(vm.GetCheckedPaths(), Is.EqualTo(new[] { "C:/b.wav", "C:/c.wav" }));
            }
            foreach (var key in new[] { Key.Left, Key.Right, Key.F6 }) Assert.That(popup.HandleKey(key), Is.True);
            Assert.That(vm.SelectionAccepted, Is.Null);
            Assert.That(preview.PlayedPaths, Is.Empty);
        }, flow: flow);

        var empty = new OHsSampleDropImportPopupVM([], new StubSampleBrowserService(), new StubSamplePreviewService(), new ImmediateUiScheduler());
        WithPopup(empty, (popup, view) =>
        {
            foreach (var key in new[] { Key.Up, Key.Down, Key.Home, Key.End, Key.Left, Key.Right, Key.F6 })
                Assert.That(popup.HandleKey(key), Is.True);
            Assert.That(((BalloonButton)popup.FindName("ImportAction")).IsEnabled, Is.False);
            Assert.That(((BalloonButton)popup.FindName("CancelAction")).IsEnabled, Is.True);
            popup.HandleKey(Key.Enter);
            Assert.That(empty.SelectionAccepted, Is.Null);
            popup.HandleKey(Key.Escape);
            Assert.That(empty.SelectionAccepted, Is.False);
            Assert.That(empty.SelectedPaths, Is.Empty);
        }, flow: flow);
    }

    [Test]
    public void ExplicitBoundsConstrainPopupSize()
    {
        var vm = new OHsSampleDropImportPopupVM([], new StubSampleBrowserService(), new StubSamplePreviewService(), new ImmediateUiScheduler());
        WithPopup(vm, (popup, view) =>
        {
            foreach (var (work, owner, expected) in new[]
                     {
                         (new Size(900, 600), new Size(640, 360), new Size(640, 360)),
                         (new Size(400, 250), new Size(700, 500), new Size(400, 250)),
                         (Size.Empty, new Size(640, 360), new Size(640, 360)),
                         (Size.Empty, Size.Empty, new Size(760, 560)),
                         (new Size(double.PositiveInfinity, 250), new Size(640, double.NaN), new Size(640, 250))
                     })
            {
                popup.ResizeToBounds(work, owner);
                Assert.That(new Size(popup.Width, popup.Height), Is.EqualTo(expected));
                Assert.That(double.IsFinite(popup.Width) && popup.Width > 0, Is.True);
                Assert.That(double.IsFinite(popup.Height) && popup.Height > 0, Is.True);
                view.UpdateLayout();
                Assert.That(((BalloonButton)popup.FindName("CancelAction")).IsEnabled, Is.True);
                Assert.That(((BalloonButton)popup.FindName("ImportAction")).IsEnabled, Is.False);
            }
        });
    }

    [Test]
    public void LargeImportRebindsRealizedRows()
    {
        var preview = new StubSamplePreviewService();
        var paths = Enumerable.Range(0, 2000).Select(i => $"C:/sample{i}.wav").ToArray();
        var vm = new OHsSampleDropImportPopupVM(paths, new StubSampleBrowserService(), preview, new ImmediateUiScheduler());
        WithPopup(vm, (popup, view) =>
        {
            var list = (ListBox)popup.FindName("FileList");
            Assert.That(list.Items.Count, Is.EqualTo(2000));
            Assert.That(list.ItemsPanel, Is.SameAs(list.FindResource("OHsVirtualizedDrawerItemsPanel")));
            Assert.That(VirtualizingPanel.GetIsVirtualizing(list), Is.True);
            Assert.That(VirtualizingPanel.GetVirtualizationMode(list), Is.EqualTo(VirtualizationMode.Recycling));
            Assert.That(ScrollViewer.GetCanContentScroll(list), Is.True);
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(1999), Is.Null);
            list.ScrollIntoView(vm.Entries[^1]);
            UiTestPump.Run(list.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);
            view.UpdateLayout();
            var last = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1999);
            Assert.That(last, Is.Not.Null);
            var row = Children(last).OfType<OHsSampleRow>().Single();
            Assert.That(row.PlayCommandParameter, Is.SameAs(vm.Entries[^1]));

            var first = vm.Entries[0];
            first.IsChecked = false;
            UiTestPump.Run(vm.LoadWaveformAsync(first));
            last.Content = first;
            view.UpdateLayout();
            row = Children(last).OfType<OHsSampleRow>().Single();
            Assert.That(row.PlayCommandParameter, Is.SameAs(first));
            Assert.That(row.Waveform, Is.SameAs(first.Waveform));
            Assert.That(Children(last).OfType<CheckBox>().Single().IsChecked, Is.False);
            Assert.That(Children(last).OfType<BalloonButton>().Count(), Is.EqualTo(1));
            Assert.That(Children(last).OfType<Control>().All(control => !control.IsTabStop), Is.True);
            var play = Children(row).OfType<BalloonButton>().Single();
            UiTestPump.Run(((IAsyncRelayCommand)play.Command).ExecuteAsync(play.CommandParameter));
            Assert.That(preview.PlayedPaths, Is.EqualTo(new[] { paths[0] }));

            list.ScrollIntoView(first);
            UiTestPump.Run(list.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);
            view.UpdateLayout();
            Assert.That(list.ItemContainerGenerator.ContainerFromIndex(0), Is.Not.Null);
        });
    }

    [TestCase("Dark", FlowDirection.LeftToRight)]
    [TestCase("Light", FlowDirection.LeftToRight)]
    [TestCase("Dark", FlowDirection.RightToLeft)]
    [TestCase("Light", FlowDirection.RightToLeft)]
    public void ImportRowsUseNeutralOwnerContractAndPreviewOnly(string theme, FlowDirection flow)
    {
        var preview = new StubSamplePreviewService();
        var vm = new OHsSampleDropImportPopupVM(["C:/a.wav"], new StubSampleBrowserService(), preview, new ImmediateUiScheduler());
        WithPopup(vm, (popup, view) =>
        {
            var list = (ListBox)popup.FindName("FileList");
            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            UiControlContract.AssertStyle<ListBoxItem>(item, "OHsSampleImportItemStyle");
            UiControlContract.AssertTemplate(item, "OHsSampleImportItemTemplate");
            Assert.That(item.Style.BasedOn, Is.SameAs(item.FindResource("PlainListBoxItemStyle")));
            var row = Children(item).OfType<OHsSampleRow>().Single();
            UiControlContract.AssertStyle<OHsSampleRow>(row, "BaseControlStyle");
            var checkbox = Children(item).OfType<CheckBox>().Single();
            UiControlContract.AssertStyle<CheckBox>(checkbox, "OHsSampleImportChoiceStyle");
            Assert.That(checkbox.Style.BasedOn, Is.SameAs(checkbox.FindResource("RegularCheckBoxStyle")));
            UiControlContract.AssertTemplate(checkbox, "RegularCheckBoxTemplate");
            var play = Children(item).OfType<BalloonButton>().Single();
            UiControlContract.AssertStyle<BalloonButton>(play, "OHsSampleRowPlayStyle");
            Assert.That(play.Style.BasedOn, Is.SameAs(play.FindResource("OHsBalloonNeutralActionStyle")));
            Assert.That(play.Command, Is.SameAs(vm.PlayCommand));
            Assert.That(play.CommandParameter, Is.SameAs(vm.Entries[0]));
            Assert.That(Children(item).OfType<BalloonComboBox>(), Is.Empty);
            Assert.That(Children(item).OfType<TextBox>(), Is.Empty);
            Assert.That(Children(item).OfType<FrameworkElement>().Any(element => element.ContextMenu is not null), Is.False);
            Assert.That(Children(item).OfType<Control>().All(control => !control.IsTabStop), Is.True);
            UiTestPump.Run(((IAsyncRelayCommand)play.Command).ExecuteAsync(play.CommandParameter));
            Assert.That(preview.PlayedPaths, Is.EqualTo(new[] { "C:/a.wav" }));
        }, theme, flow);
    }

    [Test]
    public void EqualAndLongNamesExposeDistinctPaths()
    {
        var paths = new[] { $"C:/{new string('a', 100)}/hit.wav", $"C:/{new string('b', 100)}/hit.wav" };
        var preview = new StubSamplePreviewService();
        var vm = new OHsSampleDropImportPopupVM(paths, new StubSampleBrowserService(), preview, new ImmediateUiScheduler());
        WithPopup(vm, (popup, view) =>
        {
            var list = (ListBox)popup.FindName("FileList");
            for (var i = 0; i < paths.Length; i++)
            {
                var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(i);
                var row = Children(item).OfType<OHsSampleRow>().Single();
                var checkbox = Children(item).OfType<CheckBox>().Single();
                Assert.That(row.FileName, Is.EqualTo("hit.wav"));
                Assert.That(row.FileToolTip, Is.EqualTo(paths[i]));
                Assert.That(AutomationProperties.GetName(row), Is.EqualTo(paths[i]));
                Assert.That(AutomationProperties.GetName(checkbox), Is.EqualTo(paths[i]));
                Assert.That(checkbox.ToolTip, Is.EqualTo(paths[i]));
                var play = Children(row).OfType<BalloonButton>().Single();
                UiTestPump.Run(((IAsyncRelayCommand)play.Command).ExecuteAsync(play.CommandParameter));
            }
            var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            Children(first).OfType<CheckBox>().Single().IsChecked = false;
            Assert.That(vm.GetCheckedPaths(), Is.EqualTo(new[] { paths[1] }));
            Assert.That(preview.PlayedPaths, Is.EqualTo(paths));
        });
    }

    [TestCase("Dark", false)]
    [TestCase("Light", false)]
    [TestCase("Dark", true)]
    [TestCase("Light", true)]
    public void ImportConsumersResolveFocusStylesInBothThemes(string theme, bool suppressTab)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var app = Application.Current;
        var dictionaries = app.Resources.MergedDictionaries.ToArray();
        var fonts = new[] { "Display", "Regular", "Italic", "Bold", "BoldItalic", "Light", "LightItalic" };
        var previousFonts = fonts.ToDictionary(key => key, key => app.Resources[key]);
        var vm = new OHsSampleDropImportPopupVM(new[] { "C:/a.wav" }, new StubSampleBrowserService(), new StubSamplePreviewService(), new ImmediateUiScheduler());
        OHsSampleDropImportPopup? popup = null;
        using var diagnostics = new StringWriter();
        using var listener = new System.Diagnostics.TextWriterTraceListener(diagnostics);
        var source = System.Diagnostics.PresentationTraceSources.DataBindingSource;
        var previousLevel = source.Switch.Level;
        source.Listeners.Add(listener);
        source.Switch.Level = System.Diagnostics.SourceLevels.Error;
        try
        {
            foreach (var key in fonts)
                app.Resources[key] = new System.Windows.Media.FontFamily("pack://application:,,,/Hitbench;component/Resources/Fonts/#Hitbench Noto Sans");
            app.Resources.MergedDictionaries[0] = new ResourceDictionary
            {
                Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative)
            };
            UiTestPump.Run(vm.InitializeAsync(default));
            popup = new OHsSampleDropImportPopup(vm, _ => { }) { Width = 700, Height = 500 };
            KeyboardNavigationPolicy.SetSuppressTabNavigation(popup, suppressTab);
            Assert.That(popup.ApplyTemplate(), Is.True);
            UiControlContract.AssertTemplate(popup, "BasePopupTemplate");
            using var view = new InMemoryView(popup, scheduleLayout: false);
            view.UpdateLayout();
            var list = (ListBox)popup.FindName("FileList");
            var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var checkbox = Children(container).OfType<CheckBox>().Single();
            Assert.That(checkbox.Style.BasedOn, Is.SameAs(checkbox.FindResource("RegularCheckBoxStyle")));
            Assert.That(checkbox.FocusVisualStyle, Is.SameAs(checkbox.FindResource("OHsSampleManagerKeyboardFocusStyle")));
            Assert.That(container.FocusVisualStyle, Is.SameAs(container.FindResource("DefaultFocusVisualStyle")));
            foreach (var action in Children(container).OfType<BalloonButton>().Concat(new[]
                     { (BalloonButton)popup.FindName("ImportAction"), (BalloonButton)popup.FindName("CancelAction") }))
            {
                Assert.That(action.FocusVisualStyle, Is.InstanceOf<Style>());
                Assert.That(action.FocusVisualStyle.TargetType, Is.EqualTo(typeof(Control)));
                var expected = action == popup.FindName("ImportAction") || action == popup.FindName("CancelAction")
                    ? "DefaultFocusVisualStyle" : "OHsSampleManagerKeyboardFocusStyle";
                Assert.That(action.FocusVisualStyle, Is.SameAs(action.FindResource(expected)));
                if (suppressTab) Assert.That(action.IsTabStop, Is.False);
            }
            checkbox.IsChecked = false;
            Assert.That(vm.CanConfirm, Is.False);
            if (suppressTab) Assert.That(checkbox.IsTabStop, Is.False);
            listener.Flush();
            Assert.That(diagnostics.ToString(), Is.Empty);
        }
        finally
        {
            source.Listeners.Remove(listener);
            source.Switch.Level = previousLevel;
            popup?.ReleaseOwner();
            UiTestPump.Run(vm.DeactivateAsync());
            popup?.Close();
            app.Resources.MergedDictionaries.Clear();
            foreach (var dictionary in dictionaries) app.Resources.MergedDictionaries.Add(dictionary);
            foreach (var key in fonts) app.Resources[key] = previousFonts[key];
        }
    }

    [Test]
    public void RealCheckboxBindingAndActionsRespectSelectionAndTabSuppression()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts(); var vm = new OHsSampleDropImportPopupVM(new[] { "C:/a.wav" }, new StubSampleBrowserService(), new StubSamplePreviewService(), new ImmediateUiScheduler()); UiTestPump.Run(vm.InitializeAsync(default)); bool? accepted = null; var popup = new OHsSampleDropImportPopup(vm, value => accepted = value) { Width = 700, Height = 500 }; KeyboardNavigationPolicy.SetSuppressTabNavigation(popup, true);
        using var view = new InMemoryView(popup, scheduleLayout: false); view.UpdateLayout();
        try { var list = (ListBox)popup.FindName("FileList"); var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0); var checkbox = Children(container).OfType<CheckBox>().Single(); Assert.That(checkbox.Style.BasedOn, Is.SameAs(popup.FindResource("RegularCheckBoxStyle"))); Assert.That(System.Windows.Input.KeyboardNavigation.GetIsTabStop(checkbox), Is.False); checkbox.IsChecked = false; Assert.That(vm.CanConfirm, Is.False); Assert.That(((BalloonButton)popup.FindName("ImportAction")).IsEnabled, Is.False); popup.HandleKey(System.Windows.Input.Key.Enter); Assert.That(accepted, Is.Null); checkbox.IsChecked = true; popup.HandleKey(System.Windows.Input.Key.Enter); Assert.That(accepted, Is.True); popup.HandleKey(System.Windows.Input.Key.Escape); Assert.That(accepted, Is.True); }
        finally { popup.ReleaseOwner(); UiTestPump.Run(vm.DeactivateAsync()); popup.Close(); }
    }
    private static void WithPopup(OHsSampleDropImportPopupVM vm, Action<OHsSampleDropImportPopup, InMemoryView> check,
        string theme = "Dark", FlowDirection flow = FlowDirection.LeftToRight)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var app = Application.Current;
        var dictionaries = app.Resources.MergedDictionaries.ToArray();
        OHsSampleDropImportPopup? popup = null;
        try
        {
            app.Resources.MergedDictionaries[0] = new ResourceDictionary
            {
                Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative)
            };
            UiTestPump.Run(vm.InitializeAsync(default));
            popup = new OHsSampleDropImportPopup(vm, _ => { }) { Width = 700, Height = 500, FlowDirection = flow };
            KeyboardNavigationPolicy.SetSuppressTabNavigation(popup, true);
            // InMemoryView detaches content from the Window; preserve its consumer resource scope.
            ((FrameworkElement)popup.Content).Resources.MergedDictionaries.Add(popup.Resources);
            using var view = new InMemoryView(popup, scheduleLayout: false);
            check(popup, view);
        }
        finally
        {
            popup?.ReleaseOwner();
            UiTestPump.Run(vm.DeactivateAsync());
            popup?.Close();
            app.Resources.MergedDictionaries.Clear();
            foreach (var dictionary in dictionaries) app.Resources.MergedDictionaries.Add(dictionary);
        }
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject parent) { yield return parent; for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++) foreach (var child in Children(System.Windows.Media.VisualTreeHelper.GetChild(parent, i))) yield return child; }
}
