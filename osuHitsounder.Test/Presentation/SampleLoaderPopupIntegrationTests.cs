using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
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
        Assert.That(vm.Metadata!.Name, Is.EqualTo("hit.wav"));
    }

    [Test]
    public void SelectedSummaryContainsOnlyFourFileFields()
    {
        UiTestPump.Run(vm.SelectAsync(file));
        view.UpdateLayout();
        var summary = (Grid)popup.FindName("SelectedFileDetails");
        var fields = Descendants(summary).OfType<TextBlock>().Select(block => block.Text).ToArray();
        Assert.That(fields, Does.Contain("hit.wav").And.Contain(file.FullPath));
        Assert.That(fields, Does.Contain("Size").And.Contain("Duration"));
        Assert.That(fields, Does.Contain("1.0 KiB").And.Contain("1.000 s"));
        Assert.That(fields, Does.Not.Contain("Format").And.Not.Contain("Sample rate").And.Not.Contain("Channels"));
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
    public void SeparateRowClicksReplayExactlyOnceEach()
    {
        vm.AutoPlaySamples = true;
        var row = Descendants(view.Root).OfType<TreeViewItem>().Single(item => ReferenceEquals(item.DataContext, file));
        var text = Descendants(row).OfType<TextBlock>().First(block => block.Text == file.Name);
        UiTestPump.Run(popup.HandleFileRowClickAsync(text, 1));
        UiTestPump.Run(vm.SeekAsync(.5));
        UiTestPump.Run(popup.HandleFileRowClickAsync(text, 1));
        Assert.That(preview.PlayedPaths, Is.EqualTo(new[] { file.FullPath, file.FullPath }));
        Assert.That(vm.SelectedFile, Is.SameAs(file));
        Assert.That(preview.StartPosition, Is.EqualTo(TimeSpan.Zero));
        Assert.That(file.IsChecked, Is.False);
        Assert.That(completion, Is.Null);
    }

    [Test]
    public void DoubleClickChecksWithoutRestartingOrUnchecking()
    {
        vm.AutoPlaySamples = true;
        var row = Descendants(view.Root).OfType<TreeViewItem>().Single(item => ReferenceEquals(item.DataContext, file));
        UiTestPump.Run(popup.HandleFileRowClickAsync(row, 1));
        UiTestPump.Run(vm.SeekAsync(.5));
        UiTestPump.Run(popup.HandleFileRowClickAsync(row, 2));
        UiTestPump.Run(popup.HandleFileRowClickAsync(row, 2));
        view.UpdateLayout();
        var checkbox = Descendants(row).OfType<CheckBox>().Single();
        Assert.That(file.IsChecked, Is.True);
        Assert.That(checkbox.IsChecked, Is.True);
        Assert.That(vm.CheckedCount, Is.EqualTo(1));
        Assert.That(preview.PlayedPaths, Is.EqualTo(new[] { file.FullPath }));
        Assert.That(vm.PlaybackPositionSeconds, Is.EqualTo(.5));
        Assert.That(completion, Is.Null);
    }

    [Test]
    public void RowGestureIgnoresNestedControlsFoldersAndPlaceholders()
    {
        vm.AutoPlaySamples = true;
        var checkbox = Descendants(view.Root).OfType<CheckBox>().Single(box => ReferenceEquals(box.DataContext, file));
        var root = Descendants(view.Root).OfType<TreeViewItem>().Single(item => item.DataContext is SampleBrowserEntry { Name: "Root" });
        var expander = Descendants(root).OfType<ToggleButton>().First(button => button.Name == "Expander");
        UiTestPump.Run(popup.HandleFileRowClickAsync(checkbox, 1));
        UiTestPump.Run(popup.HandleFileRowClickAsync(expander, 2));
        UiTestPump.Run(popup.HandleFileRowClickAsync(root, 2));
        Assert.That(vm.SelectedFile, Is.Null);
        Assert.That(file.IsChecked, Is.False);
        Assert.That(preview.PlayedPaths, Is.Empty);
        checkbox.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        checkbox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.That(file.IsChecked, Is.True);
        var empty = browser.Entries[browser.RootPath].Single(entry => entry.IsDirectory);
        UiTestPump.Run(vm.SelectAsync(empty));
        view.UpdateLayout();
        var placeholder = Descendants(view.Root).OfType<TreeViewItem>().Single(item => item.DataContext is SampleBrowserEntry { IsPlaceholder: true });
        var plays = preview.PlayedPaths.Count;
        UiTestPump.Run(popup.HandleFileRowClickAsync(placeholder, 2));
        Assert.That(vm.CheckedCount, Is.Zero);
        Assert.That(vm.SelectedFile, Is.Null);
        Assert.That(preview.PlayedPaths, Has.Count.EqualTo(plays));
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
    public void LongMetadataKeepsImportContract()
    {
        browser.Metadata = (path, _) => Task.FromResult(new SampleFileMetadata(path, new string('x', 400), "WAV", 1000, TimeSpan.FromSeconds(1), 44100, 1));
        UiTestPump.Run(vm.SetCheckedAsync(file, true));
        view.UpdateLayout();
        var import = (BalloonButton)popup.FindName("ImportButton");
        AssertPopupStyle<BalloonButton>(import, "SampleLoaderImportStyle");
        Assert.That(import.IsEnabled, Is.True);
        import.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.That(popup.Result, Is.EqualTo(new[] { file.FullPath }));
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
    public void ChildHeaderHoverKeepsAssignedRowContracts()
    {
        var branch = browser.Entries[browser.RootPath].Single(entry => entry.IsDirectory);
        var nestedFile = new SampleBrowserEntry(Path.Combine(branch.FullPath, "nested.wav"), "nested.wav", false);
        browser.Entries[branch.FullPath] = [nestedFile];
        UiTestPump.Run(vm.ExpandAsync(branch));
        view.UpdateLayout();
        var containers = Descendants(view.Root).OfType<TreeViewItem>().ToArray();
        var leaf = containers.Single(item => ReferenceEquals(item.DataContext, nestedFile));
        MouseOverTestState.Set(leaf, true);
        view.UpdateLayout();
        foreach (var container in containers)
        {
            AssertPopupStyle<TreeViewItem>(container, "SampleLoaderTreeItemStyle");
            Assert.That(container.Template, Is.SameAs(popup.FindResource("SampleLoaderTreeItemTemplate")));
        }
        Assert.That(branch.IsExpanded, Is.True);
        Assert.That(nestedFile.IsChecked, Is.False);
    }

    [TestCase("Dark", false)]
    [TestCase("Light", false)]
    [TestCase("Dark", true)]
    [TestCase("Light", true)]
    public void SelectedRowsKeepStyleContractDuringHeaderHover(string theme, bool derivedStyle)
    {
        Application.Current.Resources.MergedDictionaries[0].Source = new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative);
        UiTestPump.Run(vm.SelectAsync(file));
        view.UpdateLayout();
        var container = Descendants(view.Root).OfType<TreeViewItem>().Single(item => ReferenceEquals(item.DataContext, file));
        var original = (Style)popup.FindResource("SampleLoaderTreeItemStyle");
        AssertPopupStyle<TreeViewItem>(container, "SampleLoaderTreeItemStyle");
        if (derivedStyle) container.Style = new Style(typeof(TreeViewItem), original);
        MouseOverTestState.Set(container, true);
        container.SetCurrentValue(TreeViewItem.IsSelectedProperty, true);
        view.UpdateLayout();
        Assert.That(container.Style, derivedStyle ? Is.Not.SameAs(original) : Is.SameAs(original));
        if (derivedStyle) Assert.That(container.Style.BasedOn, Is.SameAs(original));
        Assert.That(container.Template, Is.SameAs(popup.FindResource("SampleLoaderTreeItemTemplate")));
        Assert.That(container.IsSelected, Is.True);
        Assert.That(vm.SelectedFile, Is.SameAs(file));
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
    public void ThemesKeepSelectedFileAndInputResourceContracts(string theme)
    {
        Application.Current.Resources.MergedDictionaries[0].Source = new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative);
        UiTestPump.Run(vm.SelectAsync(file));
        view.UpdateLayout();
        var input = (TextBox)popup.FindName("DirectoryTextBox");
        var checkbox = Descendants(view.Root).OfType<CheckBox>().Single(box => ReferenceEquals(box.DataContext, file));
        AssertPopupStyle<TextBox>(input, "SampleLoaderDirectoryInputStyle");
        Assert.That(input.Template, Is.SameAs(popup.FindResource("RoundedTextInputTemplate")));
        AssertPopupStyle<CheckBox>(checkbox, "SampleLoaderCheckStyle");
        Assert.That(checkbox.Template, Is.SameAs(popup.FindResource("SampleLoaderCheckTemplate")));
        Assert.That(input.Text, Is.EqualTo(browser.RootPath));
        Assert.That(vm.SelectedFile, Is.SameAs(file));
    }

    [Test]
    public void TreePlaybackAndFooterUseAssignedContracts()
    {
        UiTestPump.Run(vm.SelectAsync(file));
        view.UpdateLayout();
        AssertPopupStyle<TreeView>((TreeView)popup.FindName("DirectoryTree"), "SampleLoaderTreeStyle");
        AssertPopupStyle<ValueSlider>((ValueSlider)popup.FindName("PlaybackTimeline"), "SampleLoaderTimelineStyle");
        var footer = (CheckBox)popup.FindName("AutoPlayCheckBox");
        AssertPopupStyle<CheckBox>(footer, "SampleLoaderAutoPlayStyle");
        Assert.That(footer.Template, Is.SameAs(popup.FindResource("SampleLoaderAutoPlayTemplate")));
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
    public void FooterAndPlaybackKeepResourceContractsWithLongContent(string theme, FlowDirection direction)
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
            var styleKey = name switch
            {
                "AutoPlayCheckBox" => "SampleLoaderAutoPlayStyle",
                "PlaybackTimeline" => "SampleLoaderTimelineStyle",
                _ => "SampleLoaderImportStyle"
            };
            Assert.That(control.Style, Is.SameAs(popup.FindResource(styleKey)));
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

    [TestCase(false)]
    [TestCase(true)]
    public void DeepInitialFolderIsAutomaticallyBroughtIntoViewportOnce(bool loadedBeforeLayout)
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
        var tree = (TreeView)popup.FindName("DirectoryTree");
        if (loadedBeforeLayout)
        {
            tree.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, tree));
            UiTestPump.Run(popup.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
        }
        view = new InMemoryView(popup, scheduleLayout: false);
        UiTestPump.Run(popup.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
        view.UpdateLayout();
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
        Assert.That(VirtualizingPanel.GetVirtualizationMode(tree), Is.EqualTo(VirtualizationMode.Recycling));
    }

    [Test]
    public void DeactivateCancelsPendingInitialReveal()
    {
        UiTestPump.Run(popup.DeactivateAsync());
        view.Dispose();
        popup.Close();
        vm = new(browser, preview, new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        popup = new(vm, _ => { }, _ => new Size(640, 360));
        UiTestPump.Run(popup.InitializeAsync(default));
        view = new InMemoryView(popup, scheduleLayout: false);
        var revealing = popup.RevealInitialFolderAsync();
        Assert.That(revealing.IsCompleted, Is.False);
        UiTestPump.Run(popup.DeactivateAsync());
        UiTestPump.Run(revealing);
        Assert.That(vm.CanPreview, Is.False);
    }

    [Test]
    public void UnavailableInitialFolderKeepsErrorAndTreeUsable()
    {
        UiTestPump.Run(popup.DeactivateAsync());
        view.Dispose();
        popup.Close();
        vm = new(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        popup = new(vm, _ => { }, _ => new Size(640, 360));
        UiTestPump.Run(popup.InitializeAsync(Path.Combine(browser.RootPath, "missing"), default));
        view = new InMemoryView(popup, scheduleLayout: false);
        UiTestPump.Run(popup.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
        Assert.That(vm.DirectoryError, Is.EqualTo("Folder unavailable"));
        Assert.That(vm.CurrentDirectory, Is.Empty);
        Assert.That(vm.Roots, Has.Count.EqualTo(1));
        Assert.That(((BalloonButton)popup.FindName("ImportButton")).IsEnabled, Is.False);
        Assert.That(Descendants(view.Root).OfType<TextBlock>().Any(block => block.Text == "Folder unavailable"), Is.True);
    }

    private void AssertPopupStyle<T>(FrameworkElement control, string key) where T : FrameworkElement
    {
        Assert.That(control, Is.TypeOf<T>());
        Assert.That(control.Style, Is.SameAs(popup.FindResource(key)));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
