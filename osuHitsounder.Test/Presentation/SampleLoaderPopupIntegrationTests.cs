using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class SampleLoaderPopupIntegrationTests
{
    private SampleLoaderPopup popup = null!;
    private SampleLoaderPopupVM vm = null!;
    private StubSampleBrowserService browser = null!;
    private SampleBrowserEntry file = null!;
    private bool? completion;
    private InMemoryView view = null!;

    [SetUp]
    public void Setup()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        browser = new StubSampleBrowserService();
        browser.DefaultDirectory = browser.RootPath;
        file = new(Path.Combine(browser.RootPath, "hit.wav"), "hit.wav", false);
        var empty = Path.Combine(browser.RootPath, "empty");
        browser.Entries[browser.RootPath] = [file, new(empty, "empty", true)];
        browser.Entries[empty] = [];
        vm = new(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        popup = new(vm, value => completion = value, _ => new Size(640, 360));
        UiTestPump.Run(popup.InitializeAsync(CancellationToken.None));
        view = new InMemoryView(popup, scheduleLayout: false);
    }

    [TearDown]
    public void Cleanup()
    {
        UiTestPump.Run(popup.DeactivateAsync());
        view.Dispose();
        popup.Close();
    }

    [Test]
    public void CardsBindDirectoryMetadataAndCheckedCount()
    {
        Assert.That(((TextBox)popup.FindName("DirectoryTextBox")).Text, Is.EqualTo(browser.RootPath));
        Assert.That(((BalloonButton)popup.FindName("ImportButton")).IsEnabled, Is.False);
        Assert.That(((BalloonButton)popup.FindName("PreviewButton")).IsEnabled, Is.False);
        UiTestPump.Run(vm.SetCheckedAsync(file, true));
        view.UpdateLayout();
        Assert.That(((BalloonButton)popup.FindName("ImportButton")).IsEnabled, Is.True);
        Assert.That(((TextBlock)popup.FindName("CheckedCountTextBlock")).Text, Is.EqualTo("1 files checked"));
        Assert.That(((BalloonButton)popup.FindName("PreviewButton")).ButtonIcon, Is.SameAs(popup.FindResource("MediaPlay")));
        Assert.That(vm.Metadata!.Name, Is.EqualTo("hit.wav"));
    }

    [Test]
    public void CheckboxAndRowSelectionRemainIndependent()
    {
        UiTestPump.Run(vm.SelectAsync(file));
        view.UpdateLayout();
        Assert.That(file.IsChecked, Is.False);
        var checkbox = Descendants(view.Root).OfType<CheckBox>().Single(box => box.Visibility == Visibility.Visible);
        checkbox.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
        checkbox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        view.UpdateLayout();
        Assert.That(file.IsChecked, Is.True);
        Assert.That(vm.SelectedFile, Is.SameAs(file));
    }

    [Test]
    public void DirectoryEnterNavigatesWithoutImporting()
    {
        var input = (TextBox)popup.FindName("DirectoryTextBox");
        var next = Path.Combine(browser.RootPath, "empty");
        browser.Entries[next] = [];
        input.Text = next;
        using var source = new InMemoryPresentationSource();
        input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.That(vm.CurrentDirectory, Is.EqualTo(next));
        Assert.That(completion, Is.Null);
    }

    [Test]
    public void ImportSnapshotsChecksAndEscapeCancels()
    {
        UiTestPump.Run(vm.SetCheckedAsync(file, true));
        view.UpdateLayout();
        ((BalloonButton)popup.FindName("ImportButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.That(completion, Is.True);
        file.IsChecked = false;
        Assert.That(popup.Result, Is.EqualTo(new[] { file.FullPath }));
        using var source = new InMemoryPresentationSource();
        popup.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.That(completion, Is.False);
        Assert.That(popup.Result, Is.Empty);
    }

    [Test]
    public void MinimumSizeAndLongMetadataKeepFooterReachable()
    {
        browser.Metadata = (path, _) => Task.FromResult(new SampleFileMetadata(path, new string('x', 400), "WAV", 1000, TimeSpan.FromSeconds(1), 44100, 1));
        UiTestPump.Run(vm.SelectAsync(file));
        view.UpdateLayout();
        var import = (BalloonButton)popup.FindName("ImportButton");
        var bounds = import.TransformToAncestor(view.Root).TransformBounds(new Rect(import.RenderSize));
        Assert.That(popup.Width, Is.LessThanOrEqualTo(608));
        Assert.That(popup.Height, Is.LessThanOrEqualTo(328));
        Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(popup.Height));
        Assert.That(import.ActualHeight, Is.GreaterThanOrEqualTo(40));
    }

    [Test]
    public void RtlKeepsReadingOrderAndHierarchyUsable()
    {
        popup.FlowDirection = FlowDirection.RightToLeft;
        view.UpdateLayout();
        Assert.That(((TreeView)popup.FindName("DirectoryTree")).FlowDirection, Is.EqualTo(FlowDirection.RightToLeft));
        Assert.That(((TextBox)popup.FindName("DirectoryTextBox")).FlowDirection, Is.EqualTo(FlowDirection.RightToLeft));
    }

    [Test]
    public void TreeKeepsVirtualizationAndNativeSelectionSemantics()
    {
        var tree = (TreeView)popup.FindName("DirectoryTree");
        Assert.That(VirtualizingPanel.GetIsVirtualizing(tree), Is.True);
        Assert.That(VirtualizingPanel.GetVirtualizationMode(tree), Is.EqualTo(VirtualizationMode.Recycling));
        Assert.That(Descendants(view.Root).OfType<CheckBox>().Count(box => box.Visibility == Visibility.Visible), Is.EqualTo(1));
        Assert.That(tree.ItemContainerStyle.TargetType, Is.EqualTo(typeof(TreeViewItem)));
    }

    [Test]
    public void PopupAllowsTabStopsWhileOtherScreensKeepTheirKeyboardPolicy()
    {
        Assert.That(((TextBox)popup.FindName("DirectoryTextBox")).IsTabStop, Is.True);
        Assert.That(((BalloonButton)popup.FindName("ImportButton")).IsTabStop, Is.True);
        var checkbox = Descendants(view.Root).OfType<CheckBox>().Single(box => box.Visibility == Visibility.Visible);
        Assert.That(checkbox.IsTabStop, Is.True);
        var shellAction = new BalloonButton { Style = (Style)popup.FindResource("OHsBalloonNeutralActionStyle") };
        Assert.That(shellAction.IsTabStop, Is.False);
    }

    [TestCase("Dark")]
    [TestCase("Light")]
    public void ThemesAndHighContrastKeepSelectedFilesAndInputsReadable(string theme)
    {
        Application.Current.Resources.MergedDictionaries[0].Source = new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative);
        UiTestPump.Run(vm.SelectAsync(file));
        view.UpdateLayout();
        var input = (TextBox)popup.FindName("DirectoryTextBox");
        input.Style = WithHighContrast(input.Style);
        var checkbox = Descendants(view.Root).OfType<CheckBox>().Single(box => box.Visibility == Visibility.Visible);
        var container = Descendants(view.Root).OfType<TreeViewItem>().Single(item => ReferenceEquals(item.DataContext, file));
        container.Style = WithHighContrast(container.Style);
        checkbox.Style = WithHighContrast(checkbox.Style);
        view.UpdateLayout();
        Assert.That(((SolidColorBrush)input.Background).Color, Is.EqualTo(SystemColors.WindowColor));
        Assert.That(((SolidColorBrush)input.Foreground).Color, Is.EqualTo(SystemColors.WindowTextColor));
        Assert.That(((SolidColorBrush)checkbox.Foreground).Color, Is.EqualTo(SystemColors.HighlightTextColor));
    }

    private static Style WithHighContrast(Style original)
    {
        var copy = new Style(original.TargetType, original.BasedOn is null ? null : WithHighContrast(original.BasedOn));
        foreach (var setter in original.Setters) copy.Setters.Add(setter);
        BindingBase Override(BindingBase binding) => binding is Binding value &&
            value.Path?.PathParameters.OfType<System.Reflection.PropertyInfo>().Any(property =>
                property.DeclaringType == typeof(SystemParameters) && property.Name == nameof(SystemParameters.HighContrast)) == true
            ? new Binding { Source = true, Mode = BindingMode.OneWay } : binding;
        foreach (var trigger in original.Triggers)
        {
            if (trigger is DataTrigger data)
            {
                var replacement = new DataTrigger { Binding = Override(data.Binding), Value = data.Value };
                foreach (var setter in data.Setters) replacement.Setters.Add(setter);
                copy.Triggers.Add(replacement);
            }
            else if (trigger is MultiDataTrigger multi)
            {
                var replacement = new MultiDataTrigger();
                foreach (var condition in multi.Conditions) replacement.Conditions.Add(new Condition { Binding = Override(condition.Binding), Value = condition.Value });
                foreach (var setter in multi.Setters) replacement.Setters.Add(setter);
                copy.Triggers.Add(replacement);
            }
            else copy.Triggers.Add(trigger);
        }
        return copy;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
