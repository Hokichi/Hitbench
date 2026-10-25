using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class SampleLoaderPopupIntegrationTests
{
    [TestCase(1200d, 800d, false)]
    [TestCase(900d, 600d, true)]
    [TestCase(720d, 480d, true)]
    public void CompactModePreservesNavigationAndSelectionActions(double width, double height, bool compact)
    {
        var source = new StubSampleBrowserService(); source.DefaultDirectory = source.RootPath;
        var entry = new SampleBrowserEntry(Path.Combine(source.RootPath, "sample.wav"), "sample.wav", false);
        source.Entries[source.RootPath] = [entry];
        var model = new SampleLoaderPopupVM(source, new StubSamplePreviewService(), CreateScheduler(), new PlaybackVolumeSettings()) { AutoPlaySamples = false };
        var dialog = new SampleLoaderPopup(model, _ => { }, _ => new Size(width, height));
        try
        {
            UiTestPump.Run(dialog.InitializeAsync(default(CancellationToken)));
            Assert.That(dialog.Width, Is.LessThanOrEqualTo(width));
            Assert.That(dialog.Height, Is.LessThanOrEqualTo(height));
            Assert.That(dialog.IsCompactLayout, Is.EqualTo(compact));
            using var memory = new InMemoryView(dialog, scheduleLayout: false);
            UiTestPump.Run(model.SetCheckedAsync(entry, true)); memory.UpdateLayout();
            Assert.That(((BalloonButton)dialog.FindName("ImportButton")).IsEnabled, Is.True);
            var import = (BalloonButton)dialog.FindName("ImportButton");
            Assert.That(import.IsTabStop, Is.True);
            Assert.That(Hitbench.Resources.Helpers.KeyboardNavigationPolicy.GetAllowTabNavigation(import), Is.True);
            Assert.That(((ListBox)dialog.FindName("SelectedSamplesList")).Items.Cast<SampleBrowserEntry>(), Does.Contain(entry));
            var selector = (BalloonComboBox)dialog.FindName("CompactLocationSelector");
            Assert.That(selector.Visibility, Is.EqualTo(compact ? Visibility.Visible : Visibility.Collapsed));
            Assert.That(model.NavigateLocationCommand.CanExecute(model.Locations.Single(location => location.Name == "Saved Samples")), Is.True);
        }
        finally { UiTestPump.Run(dialog.DeactivateAsync()); dialog.Close(); }
    }

    [Test]
    public void LongNamesAndRightToLeftKeepSelectionAndImport()
    {
        var folder = Path.Combine(browser.RootPath, "Long names");
        var name = new string('a', 180) + ".wav";
        var entry = new SampleBrowserEntry(Path.Combine(folder, name), name, false);
        browser.Entries[folder] = [entry];
        popup.FlowDirection = FlowDirection.RightToLeft;
        UiTestPump.Run(vm.NavigateCommand.ExecuteAsync(folder));
        UiTestPump.Run(vm.SetCheckedAsync(entry, true)); view.UpdateLayout();
        Assert.That(((BalloonButton)popup.FindName("ImportButton")).IsEnabled, Is.True);
        Assert.That(((ListBox)popup.FindName("SelectedSamplesList")).Items.Cast<SampleBrowserEntry>(), Does.Contain(entry));
        Assert.That(Descendants(view.Root).OfType<TextBlock>().Any(block => Equals(block.ToolTip, entry.FullPath)), Is.True);
    }
    [TestCase(200d, 0)]
    [TestCase(180d, 1)]
    [TestCase(150d, 2)]
    [TestCase(20d, 2)]
    public void BreadcrumbOverflowUsesExplicitActionAndSeparatorWidths(double available, int first)
    {
        Assert.That(SampleLoaderPopup.GetFirstVisibleBreadcrumb(new double[] { 50, 60, 70 }, available, 30, 10), Is.EqualTo(first));
        Assert.That(SampleLoaderPopup.GetFirstVisibleBreadcrumb(Array.Empty<double>(), available, 30, 10), Is.Zero);
    }

    [Test]
    public void SharedCheckboxTemplateKeepsIndependentBindings()
    {
        view.UpdateLayout();
        var entryCheck = Descendants((ListBox)popup.FindName("DirectoryContents")).OfType<CheckBox>().Single(box => ReferenceEquals(box.DataContext, file));
        var autoPlay = (CheckBox)popup.FindName("AutoPlayCheckBox");
        Assert.That(autoPlay.Template, Is.SameAs(popup.FindResource("RegularCheckBoxTemplate")));
        Assert.That(entryCheck.Template, Is.SameAs(autoPlay.Template));
        autoPlay.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
        Assert.That(vm.AutoPlaySamples, Is.True);
        Assert.That(file.IsChecked, Is.False);
        entryCheck.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
        Assert.That(file.IsChecked, Is.True);
        autoPlay.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, false);
        Assert.That(vm.AutoPlaySamples, Is.False);
        Assert.That(file.IsChecked, Is.True);
        Assert.That(autoPlay.Content, Is.EqualTo("Auto-play on select"));
    }

    [Test]
    public void PopupUsesTheApplicationFontResource()
    {
        Assert.That(popup.FontFamily, Is.SameAs(popup.FindResource("Regular")));
        Assert.That(popup.Resources.Contains("Regular"), Is.False);
        Assert.That(popup.Resources.Contains("Bold"), Is.False);
    }

    [Test]
    public void SelectionSummaryAndPoliteCountFollowMembership()
    {
        UiTestPump.Run(vm.SetCheckedAsync(file, true));
        Assert.That(vm.SelectionAnnouncement, Is.EqualTo("1 sample selected"));
        Assert.That(vm.SelectionSummary, Is.EqualTo("1 sample"));
        var count = (TextBlock)popup.FindName("SelectedCount");
        Assert.That(System.Windows.Automation.AutomationProperties.GetLiveSetting(count), Is.EqualTo(System.Windows.Automation.AutomationLiveSetting.Polite));
        vm.ClearSelectedCommand.Execute(null);
        Assert.That(vm.SelectionAnnouncement, Is.EqualTo("0 samples selected"));
    }
    [Test]
    public void WrittenSpecUsesOwnerLocalChromeContract()
    {
        Assert.That(popup.Style, Is.SameAs(popup.FindResource("OHsSampleLoaderPopupStyle")));
        Assert.That(popup.Template, Is.SameAs(popup.FindResource("OHsSampleLoaderPopupTemplate")));
    }
    [Test]
    public void SpaceTogglesTheFocusedFileWithoutImporting()
    {
        var list = (ListBox)popup.FindName("DirectoryContents");
        list.SelectedItem = file;
        using var source = new InMemoryPresentationSource();
        list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = UIElement.PreviewKeyDownEvent });
        Assert.That(file.IsChecked, Is.True);
        list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = UIElement.PreviewKeyDownEvent });
        Assert.That(file.IsChecked, Is.False);
        Assert.That(completion, Is.Null);
    }
    [Test]
    public void FilteringThePreviewedFileRetainsItsBasketHighlight()
    {
        UiTestPump.Run(vm.SetCheckedAsync(file, true));
        view.UpdateLayout();
        var basket = (ListBox)popup.FindName("SelectedSamplesList");
        Assert.That(basket.SelectedItem, Is.SameAs(file));
        vm.SearchText = "does-not-match";
        view.UpdateLayout();
        Assert.That(basket.SelectedItem, Is.SameAs(file));
        Assert.That(vm.SelectedFile, Is.SameAs(file));
    }
    [Test]
    public void WaveformUsesExistingRendererAndSeparateTransportCommands()
    {
        UiTestPump.Run(vm.SelectAsync(file));
        view.UpdateLayout();
        Assert.That(popup.FindName("PreviewWaveform"), Is.TypeOf<WaveformView>());
        var waveform = (WaveformView)popup.FindName("PreviewWaveform");
        Assert.That(waveform.Data, Is.SameAs(vm.Waveform));
        Assert.That(waveform.SeekCommand, Is.SameAs(vm.SeekPreviewCommand));
        Assert.That(((BalloonButton)popup.FindName("PreviewButton")).Command, Is.SameAs(vm.PlayPreviewCommand));
        Assert.That(((BalloonButton)popup.FindName("StopPreviewButton")).Command, Is.SameAs(vm.StopPreviewCommand));
    }
    [Test]
    public void BasketUsesVirtualizedSelectionAndRemovalCommands()
    {
        Assert.That(popup.FindName("SelectedSamplesList"), Is.TypeOf<ListBox>());
        var list = (ListBox)popup.FindName("SelectedSamplesList");
        Assert.That(list.ItemContainerStyle, Is.SameAs(popup.FindResource("OHsSampleLoaderBasketItemStyle")));
        Assert.That(VirtualizingPanel.GetIsVirtualizing(list), Is.True);
        UiTestPump.Run(vm.SetCheckedAsync(file, true));
        view.UpdateLayout();
        Assert.That(list.Items.Cast<SampleBrowserEntry>(), Is.EqualTo(new[] { file }));
        Assert.That(((BalloonButton)popup.FindName("ClearSelectedButton")).Command, Is.SameAs(vm.ClearSelectedCommand));
        vm.ClearSelectedCommand.Execute(null);
        Assert.That(list.Items, Is.Empty);
        Assert.That(file.IsChecked, Is.False);
    }
    [Test]
    public void CompactLocationsDelegateTabWhileStandaloneCheckboxRemainsReachable()
    {
        var item = new MenuItem { Style = (Style)popup.FindResource("OHsSampleLoaderCompactLocationItemStyle") };
        item.ApplyTemplate();
        Assert.That(item.Template, Is.SameAs(popup.FindResource("OHsSampleLoaderCompactLocationItemTemplate")));
        Assert.That(item.IsCheckable, Is.True);
        Assert.That(item.IsTabStop, Is.False);
        foreach (var control in Descendants(view.Root).OfType<Control>())
            Assert.That(control.IsTabStop, Is.EqualTo(control.Name is "AutoPlayCheckBox" or "ImportButton" or "ClearSelectedButton"), control.GetType().Name);
    }
    [Test]
    public void FolderSearchUsesExistingInputContract()
    {
        var search = popup.FindName("FolderSearch");
        Assert.That(search, Is.TypeOf<TextBox>());
        var input = (TextBox)search;
        Assert.That(input.Style, Is.SameAs(popup.FindResource("OHsSampleLoaderSearchStyle")));
        Assert.That(input.Style.BasedOn, Is.SameAs(popup.FindResource("RoundedTextInputStyle")));
        input.SetCurrentValue(TextBox.TextProperty, "clap");
        Assert.That(vm.SearchText, Is.EqualTo("clap"));
    }
    private static ImmediateUiScheduler CreateScheduler()
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        return new ImmediateUiScheduler { Invocation = (action, token) =>
        {
            if (!dispatcher.CheckAccess()) return dispatcher.InvokeAsync(action, System.Windows.Threading.DispatcherPriority.DataBind, token).Task;
            token.ThrowIfCancellationRequested(); action(); return Task.CompletedTask;
        } };
    }

    private SampleLoaderPopup popup = null!;
    private SampleLoaderPopupVM vm = null!;
    private StubSampleBrowserService browser = null!;
    private SampleBrowserEntry file = null!;
    private bool? completion;
    private InMemoryView view = null!;
    private StubSamplePreviewService preview = null!;
    private SynchronizationContext? previousContext;

    [SetUp]
    public void Setup()
    {
        previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        completion = null;
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        browser = new StubSampleBrowserService();
        browser.DefaultDirectory = browser.RootPath;
        file = new(Path.Combine(browser.RootPath, "hit.wav"), "hit.wav", false);
        var empty = Path.Combine(browser.RootPath, "empty");
        browser.Entries[browser.RootPath] = [file, new(empty, "empty", true)];
        browser.Entries[empty] = [];
        preview = new();
        vm = new(browser, preview, CreateScheduler(), new PlaybackVolumeSettings()) { AutoPlaySamples = false };
        popup = new(vm, value => completion = value, _ => new Size(640, 360));
        UiTestPump.Run(popup.InitializeAsync(CancellationToken.None));
        view = new InMemoryView(popup, scheduleLayout: false);
    }

    [TearDown]
    public void Cleanup()
    {
        try { if (popup is not null) UiTestPump.Run(popup.DeactivateAsync()); }
        finally
        {
            view?.Dispose();
            popup?.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Test]
    public void BreadcrumbClickListsDrivesWithoutImporting()
    {
        var node = Descendants(view.Root).OfType<BalloonButton>().FirstOrDefault(button => button.DataContext is SampleBrowserBreadcrumb { FullPath: null });
        var overflow = (BalloonComboBox)popup.FindName("EarlierFolders");
        var earlier = overflow.ContextMenu.Items.OfType<MenuItem>().FirstOrDefault(item => item.CommandParameter is null);
        var command = node?.Command ?? earlier?.Command;
        Assert.That(command, Is.SameAs(vm.NavigateCommand));
        command!.Execute(node?.CommandParameter ?? earlier?.CommandParameter);
        UiTestPump.Run(vm.NavigateCommand.ExecutionTask!);
        view.UpdateLayout();
        Assert.That(vm.IsThisPc, Is.True);
        Assert.That(completion, Is.Null);
    }

    [Test]
    public void NoSelectionKeepsPreviewUnavailable()
    {
        var details = (Grid)popup.FindName("SelectedFileDetails");
        Assert.That(details.Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(((BalloonButton)popup.FindName("PreviewButton")).IsVisible, Is.False);
    }

    [Test]
    public void SelectionAndNavigationUpdateDetails()
    {
        var details = (Grid)popup.FindName("SelectedFileDetails");
        var completion = new TaskCompletionSource<SampleFileMetadata>();
        browser.Metadata = (_, _) => completion.Task;
        var selecting = vm.SelectAsync(file);
        view.UpdateLayout();
        Assert.That(details.Visibility, Is.EqualTo(Visibility.Visible));
        UiTestPump.Run(vm.NavigateCommand.ExecuteAsync(browser.Entries[browser.RootPath].Single(entry => entry.IsDirectory).FullPath));
        view.UpdateLayout();
        Assert.That(details.Visibility, Is.EqualTo(Visibility.Collapsed));
        completion.SetResult(new(file.FullPath, file.Name, "WAV", 100, TimeSpan.FromSeconds(1), 44100, 1));
        UiTestPump.Run(selecting);
        view.UpdateLayout();
        Assert.That(details.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void SelectionBindsDirectoryMetadataAndGatesImport()
    {
        Assert.That(vm.CurrentDirectory, Is.EqualTo(browser.RootPath));
        Assert.That(((BalloonButton)popup.FindName("ImportButton")).IsEnabled, Is.False);
        Assert.That(((BalloonButton)popup.FindName("PreviewButton")).IsEnabled, Is.False);
        UiTestPump.Run(vm.SetCheckedAsync(file, true));
        view.UpdateLayout();
        Assert.That(((BalloonButton)popup.FindName("ImportButton")).IsEnabled, Is.True);
        Assert.That(vm.CheckedCount, Is.EqualTo(1));
        Assert.That(vm.Metadata!.Name, Is.EqualTo("hit.wav"));
    }

    [Test]
    public void SelectedFileRetainsPreviewMetadata()
    {
        UiTestPump.Run(vm.SelectAsync(file));
        view.UpdateLayout();
        var summary = (Grid)popup.FindName("SelectedFileDetails");
        var fields = Descendants(summary).OfType<TextBlock>().Select(block => block.Text).ToArray();
        Assert.That(fields, Does.Contain("hit.wav"));
        Assert.That(vm.Metadata!.Length, Is.EqualTo(1000));
        Assert.That(vm.Metadata.Duration, Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(vm.PlaybackTimeText, Does.Contain("1.000 s"));
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
        var row = Descendants(view.Root).OfType<ListBoxItem>().Single(item => ReferenceEquals(item.DataContext, file));
        var text = Descendants(row).OfType<TextBlock>().First(block => block.Text == file.Name);
        UiTestPump.Run(popup.HandleEntryRowClickAsync(text, 1));
        UiTestPump.Run(vm.SeekAsync(.5));
        UiTestPump.Run(popup.HandleEntryRowClickAsync(text, 1));
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
        var row = Descendants(view.Root).OfType<ListBoxItem>().Single(item => ReferenceEquals(item.DataContext, file));
        UiTestPump.Run(popup.HandleEntryRowClickAsync(row, 1));
        UiTestPump.Run(vm.SeekAsync(.5));
        UiTestPump.Run(popup.HandleEntryRowClickAsync(row, 2));
        UiTestPump.Run(popup.HandleEntryRowClickAsync(row, 2));
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
        var checkbox = Descendants(view.Root).OfType<CheckBox>().Single(box => ReferenceEquals(box.DataContext, file));
        UiTestPump.Run(popup.HandleEntryRowClickAsync(checkbox, 1));
        Assert.That(vm.SelectedFile, Is.Null);
        var folder = Descendants(view.Root).OfType<ListBoxItem>().Single(row => row.DataContext is SampleBrowserEntry { IsDirectory: true });
        UiTestPump.Run(popup.HandleEntryRowClickAsync(folder, 1));
        Assert.That(vm.CurrentDirectory, Is.EqualTo(browser.RootPath));
        UiTestPump.Run(popup.HandleEntryRowClickAsync(folder, 2));
        view.UpdateLayout();
        Assert.That(vm.Entries, Is.Empty);
        Assert.That(vm.CanConfirm, Is.False);
        Assert.That(((ListBox)popup.FindName("DirectoryContents")).Items, Is.Empty);
    }

    [Test]
    public void DirectoryEnterNavigatesWithoutImporting()
    {
        var input = (ListBox)popup.FindName("DirectoryContents");
        input.SelectedItem = vm.Entries.Single(entry => entry.IsDirectory);
        using var source = new InMemoryPresentationSource();
        input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.That(vm.NavigateCommand.ExecutionTask, Is.Not.Null);
        UiTestPump.Run(vm.NavigateCommand.ExecutionTask!);
        Assert.That(vm.CurrentDirectory, Is.EqualTo(Path.Combine(browser.RootPath, "empty")));
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
        AssertPopupStyle<BalloonButton>(import, "OHsBalloonConfirmActionStyle");
        Assert.That(import.IsEnabled, Is.True);
        import.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.That(popup.Result, Is.EqualTo(new[] { file.FullPath }));
    }

    [Test]
    public void RtlKeepsReadingOrderAndHierarchyUsable()
    {
        popup.FlowDirection = FlowDirection.RightToLeft;
        view.UpdateLayout();
        Assert.That(((ListBox)popup.FindName("DirectoryContents")).FlowDirection, Is.EqualTo(FlowDirection.RightToLeft));
        Assert.That(((ItemsControl)popup.FindName("DirectoryBreadcrumbs")).FlowDirection, Is.EqualTo(FlowDirection.RightToLeft));
    }

    [Test]
    public void ChildHeaderHoverKeepsAssignedRowContracts()
    {
        foreach (var container in Descendants((ListBox)popup.FindName("DirectoryContents")).OfType<ListBoxItem>())
            AssertPopupStyle<ListBoxItem>(container, "SampleLoaderContentItemStyle");
    }

    [TestCase("Dark", false)]
    [TestCase("Light", false)]
    [TestCase("Dark", true)]
    [TestCase("Light", true)]
    public void SelectedRowsKeepStyleContractDuringHeaderHover(string theme, bool derivedStyle)
    {
        Application.Current.Resources.MergedDictionaries[0].Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative);
        UiTestPump.Run(vm.SelectAsync(file));
        view.UpdateLayout();
        var container = Descendants(view.Root).OfType<ListBoxItem>().Single(item => ReferenceEquals(item.DataContext, file));
        var original = (Style)popup.FindResource("SampleLoaderContentItemStyle");
        AssertPopupStyle<ListBoxItem>(container, "SampleLoaderContentItemStyle");
        if (derivedStyle) container.Style = new Style(typeof(ListBoxItem), original);
        MouseOverTestState.Set(container, true);
        container.SetCurrentValue(ListBoxItem.IsSelectedProperty, true);
        view.UpdateLayout();
        Assert.That(container.Style, derivedStyle ? Is.Not.SameAs(original) : Is.SameAs(original));
        if (derivedStyle) Assert.That(container.Style.BasedOn, Is.SameAs(original));
        Assert.That(container.Template, Is.SameAs(popup.FindResource("SampleLoaderContentItemTemplate")));
        Assert.That(container.IsSelected, Is.True);
        Assert.That(vm.SelectedFile, Is.SameAs(file));
    }

    [Test]
    public void TreeKeepsVirtualizationAndNativeSelectionSemantics()
    {
        var tree = (ListBox)popup.FindName("DirectoryContents");
        Assert.That(VirtualizingPanel.GetIsVirtualizing(tree), Is.True);
        Assert.That(VirtualizingPanel.GetVirtualizationMode(tree), Is.EqualTo(VirtualizationMode.Recycling));
        Assert.That(Descendants(view.Root).OfType<CheckBox>().Count(box => box.Visibility == Visibility.Visible && box.DataContext is SampleBrowserEntry), Is.EqualTo(1));
        Assert.That(tree.ItemContainerStyle.TargetType, Is.EqualTo(typeof(ListBoxItem)));
    }

    [Test]
    public void ConfirmationOptsIntoKeyboardNavigationWithoutChangingOtherDefaults()
    {
        Assert.That(Descendants(view.Root).OfType<BalloonButton>().First(button => button.DataContext is SampleBrowserBreadcrumb).IsTabStop, Is.False);
        Assert.That(((BalloonButton)popup.FindName("ImportButton")).IsTabStop, Is.True);
        var checkbox = Descendants(view.Root).OfType<CheckBox>().Single(box => ReferenceEquals(box.DataContext, file));
        Assert.That(checkbox.IsTabStop, Is.False);
        var shellAction = new BalloonButton { Style = (Style)popup.FindResource("OHsBalloonNeutralActionStyle") };
        Assert.That(shellAction.IsTabStop, Is.False);
    }

    [TestCase("Dark")]
    [TestCase("Light")]
    public void ThemesKeepSelectedFileAndInputResourceContracts(string theme)
    {
        Application.Current.Resources.MergedDictionaries[0].Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative);
        UiTestPump.Run(vm.SelectAsync(file));
        view.UpdateLayout();
        var input = (ItemsControl)popup.FindName("DirectoryBreadcrumbs");
        var checkbox = Descendants(view.Root).OfType<CheckBox>().Single(box => ReferenceEquals(box.DataContext, file));
        AssertPopupStyle<ItemsControl>(input, "BaseItemsControlStyle");
        AssertPopupStyle<CheckBox>(checkbox, "SampleLoaderCheckStyle");
        Assert.That(checkbox.Template, Is.SameAs(popup.FindResource("RegularCheckBoxTemplate")));
        Assert.That(vm.Breadcrumbs.Last().FullPath, Is.EqualTo(browser.RootPath));
        Assert.That(vm.SelectedFile, Is.SameAs(file));
    }

    [Test]
    public void TreePlaybackAndFooterUseAssignedContracts()
    {
        UiTestPump.Run(vm.SelectAsync(file));
        view.UpdateLayout();
        AssertPopupStyle<ListBox>((ListBox)popup.FindName("DirectoryContents"), "BaseListBoxStyle");
        Assert.That(popup.FindName("PreviewWaveform"), Is.TypeOf<WaveformView>());
        var footer = (CheckBox)popup.FindName("AutoPlayCheckBox");
        AssertPopupStyle<CheckBox>(footer, "SampleLoaderAutoPlayStyle");
        Assert.That(footer.Template, Is.SameAs(popup.FindResource("RegularCheckBoxTemplate")));
    }

    [Test]
    public void AutoPlayCheckboxWritesVmState()
    {
        var checkbox = (CheckBox)popup.FindName("AutoPlayCheckBox");
        Assert.That(checkbox, Is.Not.Null);
        Assert.That(checkbox.Content, Is.EqualTo("Auto-play on select"));
        checkbox.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
        Assert.That(vm.AutoPlaySamples, Is.True);
        Assert.That(checkbox.IsTabStop, Is.True);
        Assert.That(System.Windows.Automation.AutomationProperties.GetName(checkbox), Is.EqualTo("Auto-play on select"));
    }

    [Test]
    public void TimelineBindsBoundsPositionAndDisabledState()
    {
        var waveform=(WaveformView)popup.FindName("PreviewWaveform");
        Assert.That(waveform.IsEnabled,Is.False);
        UiTestPump.Run(vm.SelectAsync(file)); UiTestPump.Run(vm.SeekAsync(.35)); view.UpdateLayout();
        Assert.That(waveform.Data,Is.SameAs(vm.Waveform));
        Assert.That(waveform.Position,Is.EqualTo(TimeSpan.FromSeconds(.35)));
        Assert.That(waveform.IsEnabled,Is.True);
        Assert.That(((TextBlock)popup.FindName("PlaybackTimeTextBlock")).Text,Is.EqualTo(vm.PlaybackTimeText));
    }

    [TestCase("Dark", FlowDirection.LeftToRight)]
    [TestCase("Light", FlowDirection.RightToLeft)]
    public void FooterAndPlaybackKeepResourceContractsWithLongContent(string theme, FlowDirection direction)
    {
        Application.Current.Resources.MergedDictionaries[0].Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative);
        popup.FlowDirection = direction;
        browser.Metadata = (path, _) => Task.FromResult(new SampleFileMetadata(path, new string('x', 400), "WAV", 1, TimeSpan.FromSeconds(1), 44100, 2));
        UiTestPump.Run(vm.SelectAsync(file));
        vm.PreviewError = new string('e', 400);
        view.Root.InvalidateMeasure();
        view.UpdateLayout();
        foreach (var name in new[] { "AutoPlayCheckBox", "ImportButton" })
        {
            var control = (FrameworkElement)popup.FindName(name);
            Assert.That(control, Is.Not.Null);
            var styleKey = name switch
            {
                "AutoPlayCheckBox" => "SampleLoaderAutoPlayStyle",
                _ => "OHsBalloonConfirmActionStyle"
            };
            Assert.That(control.Style, Is.SameAs(popup.FindResource(styleKey)));
        }
    }

    [Test]
    public void KeyboardAndPointerTimelineChangesSeekWithoutImport()
    {
        UiTestPump.Run(vm.SetCheckedAsync(file,true)); view.UpdateLayout();
        UiTestPump.Run(popup.HandleWaveformKeyAsync(Key.Right));
        Assert.That(vm.PlaybackPositionSeconds,Is.EqualTo(.01));
        var waveform=(WaveformView)popup.FindName("PreviewWaveform");
        Assert.That(waveform.SeekAt(waveform.ActualWidth*.6),Is.True);
        UiTestPump.Run(vm.SeekPreviewCommand.ExecutionTask!);
        Assert.That(vm.PlaybackPositionSeconds,Is.EqualTo(.6).Within(.0001));
        UiTestPump.Run(popup.HandleWaveformKeyAsync(Key.Enter)); Assert.That(completion,Is.Null);
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
        Assert.That(((WaveformView)popup.FindName("PreviewWaveform")).Position, Is.EqualTo(TimeSpan.FromSeconds(.3)));
        Assert.That(preview.SeekPositions, Is.Empty);
    }

    [Test]
    public void ScrubPositionIsNotOverwrittenByMonitor()
    {
        UiTestPump.Run(vm.SelectAsync(file)); UiTestPump.Run(vm.TogglePreviewCommand.ExecuteAsync(null));view.UpdateLayout();
        var waveform=(WaveformView)popup.FindName("PreviewWaveform");
        popup.BeginWaveformScrub(); popup.UpdateWaveformScrub(.6);
        UiTestPump.Run(vm.SeekAsync(.2)); view.UpdateLayout();
        Assert.That(waveform.Position,Is.EqualTo(TimeSpan.FromSeconds(.6)));
        UiTestPump.Run(popup.CompleteWaveformScrubAsync());
        Assert.That(vm.PlaybackPositionSeconds,Is.EqualTo(.6));
        Assert.That(BindingOperations.IsDataBound(waveform,WaveformView.PositionProperty),Is.True);
    }

    [TestCase(null, false)]
    [TestCase(null, true)]
    [TestCase(@"C:\", false)]
    [TestCase(@"C:\", true)]
    [TestCase(@"\\server\share\", false)]
    [TestCase(@"\\server\share\", true)]
    public void DeepInitialFolderIsAutomaticallyBroughtIntoViewportOnce(string? browserRoot, bool loadedBeforeLayout)
    {
        var target = Path.Combine(browserRoot ?? browser.RootPath, "Deep", "Beatmap");
        browser.Entries[target] = [];
        UiTestPump.Run(vm.NavigateCommand.ExecuteAsync(target));
        view.UpdateLayout();
        Assert.That(vm.Breadcrumbs.Last().FullPath, Is.EqualTo(target));
        Assert.That(((ListBox)popup.FindName("DirectoryContents")).Items, Is.Empty);
    }

    [Test]
    public void DeactivateCancelsPendingInitialReveal()
    {
        UiTestPump.Run(popup.DeactivateAsync());
        Assert.That(vm.CanPreview, Is.False);
        Assert.That(preview.Disposed, Is.True);
    }

    [Test]
    public void UnavailableInitialFolderKeepsErrorAndTreeUsable()
    {
        UiTestPump.Run(popup.DeactivateAsync());
        view.Dispose();
        popup.Close();
        vm = new(browser, new StubSamplePreviewService(), CreateScheduler(), new PlaybackVolumeSettings());
        popup = new(vm, _ => { }, _ => new Size(640, 360));
        UiTestPump.Run(popup.InitializeAsync(Path.Combine(browser.RootPath, "missing"), default));
        view = new InMemoryView(popup, scheduleLayout: false);
        UiTestPump.Run(popup.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
        Assert.That(vm.DirectoryError, Is.EqualTo("Folder unavailable"));
        Assert.That(vm.CurrentDirectory, Is.Empty);
        Assert.That(vm.Entries, Has.Count.EqualTo(1));
        Assert.That(((BalloonButton)popup.FindName("ImportButton")).IsEnabled, Is.False);
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
