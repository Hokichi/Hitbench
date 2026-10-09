using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Xml.Linq;
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
    private StubSamplePreviewService preview = null!;

    [SetUp]
    public void Setup()
    {
        completion = null;
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        browser = new StubSampleBrowserService();
        browser.DefaultDirectory = browser.RootPath;
        file = new(Path.Combine(browser.RootPath, "hit.wav"), "hit.wav", false);
        var empty = Path.Combine(browser.RootPath, "empty");
        browser.Entries[browser.RootPath] = [file, new(empty, "empty", true)];
        browser.Entries[empty] = [];
        preview = new();
        vm = new(browser, preview, new ImmediateUiScheduler(), new PlaybackVolumeSettings()) { AutoPlaySamples = false };
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
    public void NoSelectionShowsOnlyAudioPrompt()
    {
        var prompt = (TextBlock)popup.FindName("EmptySelectionPrompt");
        var details = (Grid)popup.FindName("SelectedFileDetails");
        Assert.That(prompt.Text, Is.EqualTo("Select a .wav, .ogg or .mp3"));
        Assert.That(prompt.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(details.Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(((BalloonButton)popup.FindName("PreviewButton")).IsVisible, Is.False);
    }

    [Test]
    public void SelectionAndNavigationSwitchPromptAndDetails()
    {
        var prompt = (TextBlock)popup.FindName("EmptySelectionPrompt");
        var details = (Grid)popup.FindName("SelectedFileDetails");
        var completion = new TaskCompletionSource<SampleFileMetadata>();
        browser.Metadata = (_, _) => completion.Task;
        var selecting = vm.SelectAsync(file);
        view.UpdateLayout();
        Assert.That(prompt.Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(details.Visibility, Is.EqualTo(Visibility.Visible));
        UiTestPump.Run(vm.SelectAsync(browser.Entries[browser.RootPath].Single(entry => entry.IsDirectory)));
        view.UpdateLayout();
        Assert.That(prompt.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(details.Visibility, Is.EqualTo(Visibility.Collapsed));
        completion.SetResult(new(file.FullPath, file.Name, "WAV", 100, TimeSpan.FromSeconds(1), 44100, 1));
        UiTestPump.Run(selecting);
        view.UpdateLayout();
        Assert.That(prompt.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(details.Visibility, Is.EqualTo(Visibility.Collapsed));
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
        var checkbox = Descendants(view.Root).OfType<CheckBox>().Single(box => ReferenceEquals(box.DataContext, file));
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
    public void ChildHeaderHoverDoesNotHighlightAncestors()
    {
        var branch = browser.Entries[browser.RootPath].Single(entry => entry.IsDirectory);
        var nestedFile = new SampleBrowserEntry(Path.Combine(branch.FullPath, "nested.wav"), "nested.wav", false);
        browser.Entries[branch.FullPath] = [nestedFile];
        UiTestPump.Run(vm.ExpandAsync(branch));
        vm.Roots.Single().IsSelected = false;
        view.UpdateLayout();
        var containers = Descendants(view.Root).OfType<TreeViewItem>().ToArray();
        var leaf = containers.Single(item => ReferenceEquals(item.DataContext, nestedFile));
        var parent = containers.Single(item => ReferenceEquals(item.DataContext, branch));
        var root = containers.Single(item => ReferenceEquals(item.DataContext, vm.Roots.Single()));
        var leafRow = (Border)leaf.Template.FindName("Row", leaf);
        var parentRow = (Border)parent.Template.FindName("Row", parent);
        var rootRow = (Border)root.Template.FindName("Row", root);
        var parentRest = parentRow.Background;
        var rootRest = rootRow.Background;
        var checkbox = Descendants(leafRow).OfType<CheckBox>().Single();
        MouseOverTestState.Set(root, true);
        MouseOverTestState.Set(parent, true);
        MouseOverTestState.Set(leaf, true);
        MouseOverTestState.Set(leafRow, true);
        MouseOverTestState.Set(checkbox, true);
        view.UpdateLayout();
        Assert.That(leafRow.Background, Is.SameAs(popup.FindResource("Brush.Background.Hover")));
        Assert.That(parentRow.Background, Is.SameAs(parentRest));
        Assert.That(rootRow.Background, Is.SameAs(rootRest));
        leaf.SetCurrentValue(TreeViewItem.IsEnabledProperty, false);
        view.UpdateLayout();
        Assert.That(leafRow.Background, Is.Not.SameAs(popup.FindResource("Brush.Background.Hover")));
    }

    [TestCase("Dark", false)]
    [TestCase("Light", false)]
    [TestCase("Dark", true)]
    [TestCase("Light", true)]
    public void SelectedAndHighContrastRowsKeepStateDuringHeaderHover(string theme, bool highContrast)
    {
        Application.Current.Resources.MergedDictionaries[0].Source = new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative);
        view.UpdateLayout();
        var container = Descendants(view.Root).OfType<TreeViewItem>().Single(item => ReferenceEquals(item.DataContext, file));
        if (highContrast) container.Style = WithHighContrast(container.Style);
        view.UpdateLayout();
        var row = (Border)container.Template.FindName("Row", container);
        MouseOverTestState.Set(container, true);
        MouseOverTestState.Set(row, true);
        view.UpdateLayout();
        Assert.That(((SolidColorBrush)row.Background).Color, Is.EqualTo(highContrast ? SystemColors.ControlColor :
            ((SolidColorBrush)popup.FindResource("Brush.Background.Hover")).Color));
        if (highContrast) Assert.That(((SolidColorBrush)container.Foreground).Color, Is.EqualTo(SystemColors.ControlTextColor));
        container.SetCurrentValue(TreeViewItem.IsSelectedProperty, true);
        view.UpdateLayout();
        Assert.That(((SolidColorBrush)row.Background).Color, Is.EqualTo(highContrast ? SystemColors.HighlightColor :
            ((SolidColorBrush)popup.FindResource("Brush.Navy.Tint")).Color));
        if (highContrast) Assert.That(((SolidColorBrush)container.Foreground).Color, Is.EqualTo(SystemColors.HighlightTextColor));
    }

    [Test]
    public void TreeKeepsVirtualizationAndNativeSelectionSemantics()
    {
        var tree = (TreeView)popup.FindName("DirectoryTree");
        Assert.That(VirtualizingPanel.GetIsVirtualizing(tree), Is.True);
        Assert.That(VirtualizingPanel.GetVirtualizationMode(tree), Is.EqualTo(VirtualizationMode.Recycling));
        Assert.That(Descendants(view.Root).OfType<CheckBox>().Count(box => box.Visibility == Visibility.Visible && box.DataContext is SampleBrowserEntry), Is.EqualTo(1));
        Assert.That(tree.ItemContainerStyle.TargetType, Is.EqualTo(typeof(TreeViewItem)));
    }

    [Test]
    public void PopupAllowsTabStopsWhileOtherScreensKeepTheirKeyboardPolicy()
    {
        Assert.That(((TextBox)popup.FindName("DirectoryTextBox")).IsTabStop, Is.True);
        Assert.That(((BalloonButton)popup.FindName("ImportButton")).IsTabStop, Is.True);
        var checkbox = Descendants(view.Root).OfType<CheckBox>().Single(box => ReferenceEquals(box.DataContext, file));
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
        var checkbox = Descendants(view.Root).OfType<CheckBox>().Single(box => ReferenceEquals(box.DataContext, file));
        var container = Descendants(view.Root).OfType<TreeViewItem>().Single(item => ReferenceEquals(item.DataContext, file));
        container.Style = WithHighContrast(container.Style);
        view.UpdateLayout();
        checkbox = Descendants(view.Root).OfType<CheckBox>().Single(box => ReferenceEquals(box.DataContext, file));
        checkbox.Style = WithHighContrast(checkbox.Style);
        view.UpdateLayout();
        Assert.That(((SolidColorBrush)input.Background).Color, Is.EqualTo(SystemColors.WindowColor));
        Assert.That(((SolidColorBrush)input.Foreground).Color, Is.EqualTo(SystemColors.WindowTextColor));
        Assert.That(((SolidColorBrush)checkbox.Foreground).Color, Is.EqualTo(SystemColors.HighlightTextColor));
    }

    [Test]
    public void TreeCardIsAbovePlaybackAndFooter()
    {
        UiTestPump.Run(vm.SelectAsync(file));
        view.Root.InvalidateMeasure();
        view.UpdateLayout();
        var tree = (TreeView)popup.FindName("DirectoryTree");
        var timeline = (ValueSlider)popup.FindName("PlaybackTimeline");
        Assert.That(timeline, Is.Not.Null);
        var treeBounds = tree.TransformToAncestor(view.Root).TransformBounds(new Rect(tree.RenderSize));
        var playbackBounds = timeline.TransformToAncestor(view.Root).TransformBounds(new Rect(timeline.RenderSize));
        var footer = (CheckBox)popup.FindName("AutoPlayCheckBox");
        var footerBounds = footer.TransformToAncestor(view.Root).TransformBounds(new Rect(footer.RenderSize));
        Assert.That(treeBounds.Bottom, Is.LessThanOrEqualTo(playbackBounds.Top));
        Assert.That(playbackBounds.Bottom, Is.LessThanOrEqualTo(footerBounds.Top));
        Assert.That(treeBounds.Width, Is.GreaterThan(popup.Width * .7));
    }

    [Test]
    public void AutoPlayCheckboxWritesVmState()
    {
        var checkbox = (CheckBox)popup.FindName("AutoPlayCheckBox");
        Assert.That(checkbox, Is.Not.Null);
        Assert.That(checkbox.Content, Is.EqualTo("Auto-play samples"));
        checkbox.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
        Assert.That(vm.AutoPlaySamples, Is.True);
        Assert.That(checkbox.IsTabStop, Is.True);
        Assert.That(checkbox.ActualHeight, Is.GreaterThanOrEqualTo(40));
        Assert.That(System.Windows.Automation.AutomationProperties.GetName(checkbox), Is.EqualTo("Auto-play samples"));
    }

    [Test]
    public void TimelineBindsBoundsPositionAndDisabledState()
    {
        var timeline = (ValueSlider)popup.FindName("PlaybackTimeline");
        Assert.That(timeline, Is.Not.Null);
        Assert.That(timeline.IsEnabled, Is.False);
        UiTestPump.Run(vm.SelectAsync(file));
        UiTestPump.Run(vm.SeekAsync(.35));
        view.UpdateLayout();
        Assert.That(timeline.Minimum, Is.Zero);
        Assert.That(timeline.Maximum, Is.EqualTo(1));
        Assert.That(timeline.Value, Is.EqualTo(.35));
        Assert.That(timeline.IsEnabled, Is.True);
        Assert.That(timeline.IsTabStop, Is.True);
        Assert.That(((TextBlock)popup.FindName("PlaybackTimeTextBlock")).Text, Is.EqualTo(vm.PlaybackTimeText));
    }

    [TestCase("Dark", FlowDirection.LeftToRight)]
    [TestCase("Light", FlowDirection.RightToLeft)]
    public void FooterAndPlaybackRemainReachableWithLongContent(string theme, FlowDirection direction)
    {
        Application.Current.Resources.MergedDictionaries[0].Source = new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative);
        popup.FlowDirection = direction;
        browser.Metadata = (path, _) => Task.FromResult(new SampleFileMetadata(path, new string('x', 400), "WAV", 1, TimeSpan.FromSeconds(1), 44100, 2));
        UiTestPump.Run(vm.SelectAsync(file));
        vm.PreviewError = new string('e', 400);
        view.Root.InvalidateMeasure();
        view.UpdateLayout();
        foreach (var name in new[] { "AutoPlayCheckBox", "PlaybackTimeline", "ImportButton" })
        {
            var control = (FrameworkElement)popup.FindName(name);
            Assert.That(control, Is.Not.Null);
            var bounds = control.TransformToAncestor(view.Root).TransformBounds(new Rect(control.RenderSize));
            Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(popup.Height));
            Assert.That(bounds.Right, Is.LessThanOrEqualTo(popup.Width));
        }
    }

    [Test]
    public void KeyboardAndPointerTimelineChangesSeekWithoutImport()
    {
        UiTestPump.Run(vm.SetCheckedAsync(file, true));
        view.Root.InvalidateMeasure();
        view.UpdateLayout();
        var timeline = (ValueSlider)popup.FindName("PlaybackTimeline");
        Slider.IncreaseSmall.Execute(null, timeline);
        Assert.That(vm.PlaybackPositionSeconds, Is.EqualTo(.01));
        var thumb = Descendants(timeline).OfType<Thumb>().Single();
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0));
        timeline.SetCurrentValue(RangeBase.ValueProperty, .6);
        thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false));
        Assert.That(vm.PlaybackPositionSeconds, Is.EqualTo(.6));
        using var source = new InMemoryPresentationSource();
        timeline.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        Assert.That(completion, Is.Null);
    }

    [Test]
    public void PlaybackUpdatesDoNotTriggerSeek()
    {
        UiTestPump.Run(vm.SelectAsync(file));
        UiTestPump.Run(vm.TogglePreviewCommand.ExecuteAsync(null));
        preview.Position = TimeSpan.FromSeconds(.3);
        var updated = new TaskCompletionSource();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.PlaybackPositionSeconds) && vm.PlaybackPositionSeconds == .3) updated.TrySetResult();
        };
        UiTestPump.Run(updated.Task);
        view.UpdateLayout();
        Assert.That(((ValueSlider)popup.FindName("PlaybackTimeline")).Value, Is.EqualTo(.3));
        Assert.That(preview.SeekPositions, Is.Empty);
    }

    [Test]
    public void ScrubPositionIsNotOverwrittenByMonitor()
    {
        UiTestPump.Run(vm.SelectAsync(file));
        UiTestPump.Run(vm.TogglePreviewCommand.ExecuteAsync(null));
        view.Root.InvalidateMeasure();
        view.UpdateLayout();
        var timeline = (ValueSlider)popup.FindName("PlaybackTimeline");
        var thumb = Descendants(timeline).OfType<Thumb>().Single();
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0));
        timeline.SetCurrentValue(RangeBase.ValueProperty, .6);
        UiTestPump.Run(vm.SeekAsync(.2));
        Assert.That(timeline.Value, Is.EqualTo(.6));
        var scrubbed = new TaskCompletionSource();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.PlaybackPositionSeconds) && vm.PlaybackPositionSeconds == .6) scrubbed.TrySetResult();
        };
        thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false));
        UiTestPump.Run(scrubbed.Task);
        UiTestPump.Run(popup.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
        Assert.That(vm.PlaybackPositionSeconds, Is.EqualTo(.6));
        Assert.That(BindingOperations.IsDataBound(timeline, RangeBase.ValueProperty), Is.True);
    }

    [Test]
    public void DeepInitialFolderIsBroughtIntoViewportOnce()
    {
        UiTestPump.Run(popup.DeactivateAsync());
        view.Dispose();
        popup.Close();
        browser = new();
        var branches = Enumerable.Range(0, 120).Select(index => new SampleBrowserEntry(Path.Combine(browser.RootPath, $"Folder{index:000}"), $"Folder{index:000}", true)).ToArray();
        var parent = branches[^1];
        var target = new SampleBrowserEntry(Path.Combine(parent.FullPath, "Beatmap"), "Beatmap", true);
        browser.Entries[browser.RootPath] = branches;
        browser.Entries[parent.FullPath] = [target];
        browser.Entries[target.FullPath] = [];
        vm = new(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        popup = new(vm, _ => { }, _ => new Size(640, 360));
        UiTestPump.Run(popup.InitializeAsync(target.FullPath, default));
        view = new InMemoryView(popup, scheduleLayout: false);
        UiTestPump.Run(popup.RevealInitialFolderAsync());
        view.UpdateLayout();
        var tree = (TreeView)popup.FindName("DirectoryTree");
        var selected = Descendants(tree).OfType<TreeViewItem>().Single(item => ReferenceEquals(item.DataContext, target));
        var header = (Border)selected.Template.FindName("Row", selected);
        var bounds = header.TransformToAncestor(tree).TransformBounds(new Rect(header.RenderSize));
        Assert.That(bounds.Top, Is.GreaterThanOrEqualTo(0));
        Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(tree.ActualHeight));
        var scroll = Descendants(tree).OfType<ScrollViewer>().First();
        scroll.ScrollToTop();
        view.UpdateLayout();
        UiTestPump.Run(popup.RevealInitialFolderAsync());
        Assert.That(scroll.VerticalOffset, Is.Zero);
        Assert.That(VirtualizingPanel.GetIsVirtualizing(tree), Is.True);
    }

    [Test]
    public void DeactivateCancelsPendingInitialReveal()
    {
        var revealing = popup.RevealInitialFolderAsync();
        UiTestPump.Run(popup.DeactivateAsync());
        UiTestPump.Run(revealing);
        Assert.That(vm.CanPreview, Is.False);
    }

    private static Style WithHighContrast(Style original)
    {
        var copy = new Style(original.TargetType, original.BasedOn is null ? null : WithHighContrast(original.BasedOn));
        foreach (var setter in original.Setters)
            if (setter is Setter { Property: var property, Value: ControlTemplate template } && property == Control.TemplateProperty)
                copy.Setters.Add(new Setter(property, WithHighContrast(template)));
            else copy.Setters.Add(setter);
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

    private static ControlTemplate WithHighContrast(ControlTemplate original)
    {
        // Icon initializes its own resources; do not deserialize those a second time.
        // This serializes runtime controls, never repository source.
        var markup = XDocument.Parse(System.Windows.Markup.XamlWriter.Save(original));
        markup.Descendants().Where(element => element.Name.LocalName == "Icon.Resources").Remove();
        var copy = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(markup.ToString());
        foreach (var trigger in copy.Triggers)
        {
            if (trigger is DataTrigger data && IsHighContrastBinding(data.Binding))
                data.Binding = new Binding { Source = true, Mode = BindingMode.OneWay };
            if (trigger is MultiDataTrigger multi)
                foreach (var condition in multi.Conditions)
                    if (IsHighContrastBinding(condition.Binding))
                        condition.Binding = new Binding { Source = true, Mode = BindingMode.OneWay };
        }
        return copy;
    }

    private static bool IsHighContrastBinding(BindingBase binding) => binding is Binding value &&
        (value.Path?.PathParameters.OfType<System.Reflection.PropertyInfo>().Any(property =>
            property.DeclaringType == typeof(SystemParameters) && property.Name == nameof(SystemParameters.HighContrast)) == true ||
         value.Path?.Path.Contains("HighContrast", StringComparison.Ordinal) == true);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
