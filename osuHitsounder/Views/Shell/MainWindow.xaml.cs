using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.Ui;
using osuHitsounder.Views.Pages;
using osuHitsounder.ViewModels.Shell.HomePage;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Views.Popups;
using osuHitsounder.ViewModels.Shell;
using CommunityToolkit.Mvvm.Input;

namespace osuHitsounder;

public partial class MainWindow : Window, IRecipient<ProjectCreatedMessage>,
    IRecipient<ProjectOpenRequestedMessage>, IRecipient<ProjectCreationFailedMessage>, IPopupHost
{
    private readonly PopupOverlayHandoffState popupOverlayState = new();
    private readonly DispatcherTimer popupOverlayTimer;
    private int popupHideGeneration;
    private bool popupHandoffPending;
    private static readonly DependencyPropertyKey IsPopupOverlayVisiblePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsPopupOverlayVisible), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsPopupOverlayVisibleProperty = IsPopupOverlayVisiblePropertyKey.DependencyProperty;
    public bool IsPopupOverlayVisible => (bool)GetValue(IsPopupOverlayVisibleProperty);

    public void ShowPopupOverlay()
    {
        CancelBackgroundDrag();
        popupOverlayTimer.Stop();
        popupHandoffPending = false;
        if (popupOverlayState.OnPopupShown() == PopupOverlayHostAction.ShowOverlay)
            SetValue(IsPopupOverlayVisiblePropertyKey, true);
    }

    public void BeginPopupHandoff() => popupHandoffPending = popupOverlayState.ActivePopupCount > 0;

    public void HidePopupOverlay()
    {
        if (popupHandoffPending)
        {
            popupHandoffPending = false;
            HidePopupOverlayForHandoff();
        }
        else if (popupOverlayState.OnPopupHidden() == PopupOverlayHostAction.HideOverlay)
            SetValue(IsPopupOverlayVisiblePropertyKey, false);
    }

    public void HidePopupOverlayForHandoff()
    {
        if (popupOverlayState.OnPopupHiddenForHandoff(out popupHideGeneration) == PopupOverlayHostAction.DeferHide)
        {
            popupOverlayTimer.Stop();
            popupOverlayTimer.Start();
        }
    }

    private void OnPopupOverlayTimerTick(object? sender, EventArgs e)
    {
        popupOverlayTimer.Stop();
        if (popupOverlayState.ResolveDeferredHide(popupHideGeneration) == PopupOverlayHostAction.HideOverlay)
            SetValue(IsPopupOverlayVisiblePropertyKey, false);
    }
    private HomePage page;
    private readonly IMessenger messenger;
    private readonly Func<WorkstationPage> createWorkstation;
    private readonly Func<HomePage> createHome;
    private Task returnHome = Task.CompletedTask;
    private bool closeRequested;
    private readonly CancellationTokenSource lifetime = new();
    private Task? loading;
    private bool closed;
    private HomePageVM viewModel;
    private readonly IAppSettingsService settings;
    private readonly DispatcherTimer placementTimer;
    private IInputElement? priorFocus;
    private DispatcherOperation? focusRestore;
    private Task placementSave = Task.CompletedTask;
    private double restoredWidth = 1280;
    private double restoredHeight = 720;
    private int placementVersion;
    private bool placementInitialized;
    private bool placementDirty;
    private bool lastIsMaximized;
    private bool closingForSave;
    private bool closingAfterSave;
    private readonly Action<string> reportError;
    private readonly Action<WindowState> requestWindowState;
    private readonly Func<Window, Size> getWorkArea;
    private readonly double defaultMinWidth;
    private readonly double defaultMinHeight;
    private DispatcherOperation? layoutRefresh;
    private (Size Size, double Header, object? Content, double MinimumWidth, double MinimumHeight)? lastShellLayout;
    internal long SongsLayoutRefreshCount { get; private set; }
    private readonly OHsWindowFrame nativeFrame;
    private readonly Action startDrag;
    private readonly Action<Rect> moveRestoredWindow;
    private readonly Func<bool> isLeftButtonPressed;
    private bool restoringBackgroundDrag;
    private Point? backgroundDragOrigin;
    private static readonly DependencyPropertyKey IsAcrylicEnabledPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsAcrylicEnabled), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsAcrylicEnabledProperty = IsAcrylicEnabledPropertyKey.DependencyProperty;
    public bool IsAcrylicEnabled => (bool)GetValue(IsAcrylicEnabledProperty);

    public MainWindow(HomePage page, IMessenger messenger, Func<WorkstationPage> createWorkstation,
        IAppSettingsService settings, Func<HomePage> createHome)
        : this(page, messenger, createWorkstation, (HomePageVM)page.DataContext, settings, null, createHome: createHome) { }

    internal MainWindow(HomePage page, IMessenger messenger, Func<WorkstationPage> createWorkstation,
        HomePageVM viewModel, IAppSettingsService settings, Action<string>? reportError,
        Action<WindowState>? requestWindowState = null, Func<Window, Size>? getWorkArea = null,
        Func<HomePage>? createHome = null, Action? startDrag = null,
        Action<Rect>? moveRestoredWindow = null, Func<bool>? isLeftButtonPressed = null)
    {
        this.reportError = reportError ?? (error => MessageBox.Show(this, error, "osuHitsounder",
            MessageBoxButton.OK, MessageBoxImage.Error));
        this.requestWindowState = requestWindowState ?? (state =>
        {
            if (state == WindowState.Minimized) SystemCommands.MinimizeWindow(this);
            else if (state == WindowState.Maximized) SystemCommands.MaximizeWindow(this);
            else SystemCommands.RestoreWindow(this);
        });
        this.page = page;
        this.viewModel = viewModel;
        this.messenger = messenger;
        this.createWorkstation = createWorkstation;
        this.createHome = createHome ?? (() => throw new InvalidOperationException("A home-page factory is required to return home."));
        this.settings = settings;
        this.getWorkArea = getWorkArea ?? WindowWorkArea.GetClientSize;
        InitializeComponent();
        ShellHeader.SetBinding(osuHitsounder.Views.Shell.OHsShellHeader.CanGoBackProperty,
            new System.Windows.Data.Binding("Content.DataContext.CanGoBack")
            { Source = PageHost, Mode = System.Windows.Data.BindingMode.OneWay });
        popupOverlayTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(200) };
        popupOverlayTimer.Tick += OnPopupOverlayTimerTick;
        nativeFrame = new OHsWindowFrame(this, enabled => SetValue(IsAcrylicEnabledPropertyKey, enabled));
        this.startDrag = startDrag ?? DragMove;
        this.moveRestoredWindow = moveRestoredWindow ?? nativeFrame.MoveRestoredWindow;
        this.isLeftButtonPressed = isLeftButtonPressed ?? (() => Mouse.LeftButton == MouseButtonState.Pressed);
        ShellRoot.MouseMove += OnBackgroundMouseMove;
        ShellRoot.MouseUp += OnBackgroundMouseUp;
        ShellRoot.LostMouseCapture += OnBackgroundLostMouseCapture;
        ComponentDispatcher.EnterThreadModal += OnEnterThreadModal;
        InputManager.Current.PreProcessInput += OnPreProcessInput;
        defaultMinWidth = MinWidth;
        defaultMinHeight = MinHeight;
        placementTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        placementTimer.Tick += OnPlacementTimerTick;
        DataContext = viewModel;
        PageHost.Content = page;
        viewModel.PropertyChanged += OnStateChanged;
        ProcessingText.IsVisibleChanged += OnProcessingVisibilityChanged;
        messenger.Register<ProjectCreatedMessage>(this);
        messenger.Register<ProjectOpenRequestedMessage>(this);
        messenger.Register<ProjectCreationFailedMessage>(this);
        SizeChanged += OnWindowSizeChanged;
        StateChanged += OnWindowStateChanged;
        LocationChanged += OnLayoutEnvironmentChanged;
        SourceInitialized += OnLayoutEnvironmentChanged;
        LayoutUpdated += OnShellLayoutUpdated;
        DpiChanged += OnWindowDpiChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    public string? FatalError => page.FatalError;
    public bool RequiresShutdown => page.RequiresShutdown;

    public Task LoadAsync() => loading ??= LoadPageAsync();

    internal Task ReturnHomeAsync()
    {
        if (!returnHome.IsCompleted) return returnHome;
        if (closed || closingForSave || closeRequested || PageHost.Content is not WorkstationPage workstation)
            return Task.CompletedTask;
        return returnHome = ReturnHomeCoreAsync(workstation);
    }

    private async Task ReturnHomeCoreAsync(WorkstationPage workstation)
    {
        HomePage? next = null;
        try
        {
            if (!await workstation.PrepareToReturnAsync(lifetime.Token) || closed || closeRequested) return;
            next = createHome();
            await workstation.DeactivateAsync();
            if (closed || closeRequested) { next.Deactivate(); return; }
            viewModel.PropertyChanged -= OnStateChanged;
            focusRestore?.Abort();
            priorFocus = null;
            page = next;
            viewModel = (HomePageVM)next.DataContext;
            DataContext = viewModel;
            viewModel.PropertyChanged += OnStateChanged;
            PageHost.Content = next;
            ShellHeader.IsWorkstationVisible = false;
            MinWidth = defaultMinWidth;
            MinHeight = defaultMinHeight;
            QueueSongsLayout();
            loading = LoadPageAsync();
            await loading;
            if (!closed && page.FatalError is { } error) reportError(error);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { next?.Deactivate(); }
        catch (Exception error)
        {
            if (!ReferenceEquals(PageHost.Content, next)) next?.Deactivate();
            workstation.CancelReturn();
            if (!closed) reportError($"Unable to return home: {error.Message}");
        }
    }

    internal async Task<bool> PrepareProjectForCloseAsync()
    {
        closeRequested = true;
        if (SampleManagerHost.Content is System.Windows.Controls.UserControl drawer) DismissSampleManager(drawer);
        try
        {
            await returnHome;
            if (PageHost.Content is WorkstationPage workstation)
            {
                await workstation.SettleInstrumentalEditorAsync();
                await DrawerInputCompletion;
                await workstation.FlushSavesAsync();
                await workstation.DeactivateAsync();
            }
            return true;
        }
        catch (Exception error)
        {
            closeRequested = false;
            if (PageHost.Content is WorkstationPage workstation) workstation.CancelReturn();
            reportError($"Unable to save project: {error.Message}");
            return false;
        }
    }

    public void ApplySettings(AppSettings value)
    {
        restoredWidth = NormalizeDimension(value.RestoredWidth, 1280, MinWidth);
        restoredHeight = NormalizeDimension(value.RestoredHeight, 720, MinHeight);
        Width = restoredWidth;
        Height = restoredHeight;
        lastIsMaximized = value.IsMaximized;
        WindowState = value.IsMaximized ? WindowState.Maximized : WindowState.Normal;
        placementInitialized = true;
        UpdateMaximizeRestoreButton();
    }

    public void Receive(ProjectCreatedMessage message) => OpenWorkstation(message.ProjectPath);

    public void Receive(ProjectOpenRequestedMessage message) => OpenWorkstation(message.ProjectPath);

    private void OpenWorkstation(string projectPath)
    {
        if (closed || closeRequested || closingForSave || !returnHome.IsCompleted || PageHost.Content != page)
            return;
        try
        {
            var workstation = createWorkstation();
            workstation.SetProjectPath(projectPath);
            page.Deactivate();
            PageHost.Content = workstation;
            ShellHeader.IsWorkstationVisible = true;
            MinWidth = defaultMinWidth;
            MinHeight = defaultMinHeight;
        }
        catch (Exception error)
        {
            reportError($"Unable to open workstation: {error.Message}");
        }
    }

    public void Receive(ProjectCreationFailedMessage message)
    {
        if (!closed && PageHost.Content == page)
            reportError(message.Error);
    }

    private async Task LoadPageAsync()
    {
        try { await page.LoadAsync(lifetime.Token); }
        catch (OperationCanceledException) when (!closed)
        {
            // Navigation owns the cancellation of unfinished home-page loading.
        }
        finally
        {
            if (closed)
                lifetime.Dispose();
        }
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == WindowStateProperty) CancelBackgroundDrag();
    }

    protected override void OnClosed(EventArgs e)
    {
        clearSampleFocusRestore?.Invoke();
        CancelBackgroundDrag();
        ComponentDispatcher.EnterThreadModal -= OnEnterThreadModal;
        ShellRoot.MouseMove -= OnBackgroundMouseMove;
        ShellRoot.MouseUp -= OnBackgroundMouseUp;
        ShellRoot.LostMouseCapture -= OnBackgroundLostMouseCapture;
        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        closed = true;
        popupOverlayTimer.Stop();
        popupOverlayTimer.Tick -= OnPopupOverlayTimerTick;
        nativeFrame.Dispose();
        placementTimer.Stop();
        placementTimer.Tick -= OnPlacementTimerTick;
        SizeChanged -= OnWindowSizeChanged;
        StateChanged -= OnWindowStateChanged;
        LocationChanged -= OnLayoutEnvironmentChanged;
        SourceInitialized -= OnLayoutEnvironmentChanged;
        LayoutUpdated -= OnShellLayoutUpdated;
        DpiChanged -= OnWindowDpiChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        layoutRefresh?.Abort();
        focusRestore?.Abort();
        viewModel.PropertyChanged -= OnStateChanged;
        ProcessingText.IsVisibleChanged -= OnProcessingVisibilityChanged;
        messenger.UnregisterAll(this);
        lifetime.Cancel();
        page.Deactivate();
        if (loading is null || loading.IsCompleted)
            lifetime.Dispose();
        base.OnClosed(e);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || closingAfterSave ||
            (!placementDirty && placementSave.IsCompleted && returnHome.IsCompleted &&
             (PageHost.Content is not WorkstationPage workstationPage || !workstationPage.HasPendingSaves))) return;
        e.Cancel = true;
        if (closingForSave) return;
        closingForSave = true;
        placementTimer.Stop();
        if (!await PrepareProjectForCloseAsync())
        {
            closingForSave = false;
            return;
        }
        try
        {
            await placementSave;
            placementTimer.Stop();
            if (placementDirty)
                await settings.SaveWindowPlacementAsync(lastIsMaximized, restoredWidth, restoredHeight,
                    lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Trace.TraceError("Window placement save failed during close: {0}", error);
        }
        finally
        {
            placementDirty = false;
            closingAfterSave = true;
            await Dispatcher.Yield(DispatcherPriority.Background);
            Close();
        }
    }

    private static double NormalizeDimension(double value, double fallback, double minimum) =>
        Math.Max(minimum, double.IsFinite(value) && value > 0 ? value : fallback);

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        QueueSongsLayout();
        RecordWindowSize(ActualWidth, ActualHeight);
    }

    private void OnLayoutEnvironmentChanged(object? sender, EventArgs e) => QueueSongsLayout();
    private (Size, double, object?, double, double) GetShellLayout() =>
        (ShellRoot.RenderSize, ShellLayout.RowDefinitions[0].ActualHeight,
            PageHost.Content, page.MinWidth, page.MinHeight);
    private void OnShellLayoutUpdated(object? sender, EventArgs e)
    {
        if (lastShellLayout != GetShellLayout()) QueueSongsLayout();
    }
    private void OnWindowDpiChanged(object sender, DpiChangedEventArgs e) => QueueSongsLayout();
    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e) => QueueSongsLayout();

    private void QueueSongsLayout()
    {
        if (closed || PageHost.Content != page || layoutRefresh?.Status == DispatcherOperationStatus.Pending) return;
        layoutRefresh = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (closed || PageHost.Content != page || page.ActualWidth <= 0) return;
            var size = getWorkArea(this);
            SongsLayoutRefreshCount++;
            if (size.IsEmpty || size.Width <= 0 || size.Height <= 0) return;
            // Named shell content uses a border and a header row. Measure their public layout values.
            var root = ShellRoot;
            var grid = ShellLayout;
            var header = grid.RowDefinitions[0].ActualHeight;
            page.UpdateSongsLayout(new Size(size.Width, Math.Max(0, size.Height - header)));
            var width = page.MinWidth + root.BorderThickness.Left + root.BorderThickness.Right;
            var height = page.MinHeight + header + root.BorderThickness.Top + root.BorderThickness.Bottom;
            if (Math.Abs(MinWidth - width) > .01) MinWidth = width;
            if (Math.Abs(MinHeight - height) > .01) MinHeight = height;
            lastShellLayout = GetShellLayout();
        }));
    }

    internal void RecordWindowSize(double width, double height)
    {
        if (!placementInitialized || closed || restoringBackgroundDrag || WindowState != WindowState.Normal) return;
        restoredWidth = width;
        restoredHeight = height;
        QueuePlacementSave();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        QueueSongsLayout();
        RecordWindowState(ActualWidth, ActualHeight);
    }

    internal void RecordWindowState(double width, double height)
    {
        UpdateMaximizeRestoreButton();
        if (!placementInitialized || closed || WindowState == WindowState.Minimized) return;
        lastIsMaximized = WindowState == WindowState.Maximized;
        if (!lastIsMaximized && !restoringBackgroundDrag)
        {
            restoredWidth = width;
            restoredHeight = height;
        }
        QueuePlacementSave();
    }

    private void QueuePlacementSave()
    {
        placementDirty = true;
        placementVersion++;
        placementTimer.Stop();
        placementTimer.Start();
    }

    private async void OnPlacementTimerTick(object? sender, EventArgs e)
    {
        placementTimer.Stop();
        if (!placementDirty || !placementSave.IsCompleted) return;
        var version = placementVersion;
        placementSave = SavePlacementAsync(version, lastIsMaximized, restoredWidth, restoredHeight);
        await placementSave;
    }

    private async Task SavePlacementAsync(int version, bool isMaximized, double width, double height)
    {
        try
        {
            await settings.SaveWindowPlacementAsync(isMaximized, width, height, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Trace.TraceError("Window placement save failed: {0}", error);
        }
        finally
        {
            if (version == placementVersion) placementDirty = false;
            else if (!closed && !closingForSave) placementTimer.Start();
        }
    }

    private void OnBackgroundMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject origin ||
            !nativeFrame.TryGetDragGeometry(out var cursor, out var bounds, out var dpiX, out var dpiY)) return;
        e.Handled = HandleBackgroundMouseDown(origin, e.ClickCount, e.LeftButton == MouseButtonState.Pressed,
            cursor, bounds, dpiX, dpiY);
    }

    internal bool HandleBackgroundMouseDown(DependencyObject originalSource, int clickCount, bool leftPressed,
        Point cursorPixels, Rect currentPixels, double dpiX, double dpiY)
    {
        CancelBackgroundDrag();
        if (!ReferenceEquals(originalSource, ShellRoot) || !leftPressed || !CanDragBackground) return false;
        if (clickCount == 2) { ToggleMaximizeRestore(); return true; }
        if (WindowState == WindowState.Maximized)
        {
            backgroundDragOrigin = cursorPixels;
            if (PresentationSource.FromVisual(ShellRoot) is not null) ShellRoot.CaptureMouse();
            return true;
        }
        BeginBackgroundDrag(cursorPixels, currentPixels, dpiX, dpiY);
        return true;
    }

    private bool CanDragBackground => !closed && !viewModel.IsInteractionBlocked && !viewModel.IsProcessing &&
        !IsPopupOverlayVisible && !IsBlockingDrawerVisible && !ComponentDispatcher.IsThreadModal;

    private void BeginBackgroundDrag(Point cursorPixels, Rect currentPixels, double dpiX, double dpiY)
    {
        var maximized = WindowState == WindowState.Maximized;
        var restored = maximized ? OHsWindowFrame.GetRestoreBounds(cursorPixels, currentPixels,
            new Size(restoredWidth, restoredHeight), dpiX, dpiY) : Rect.Empty;
        restoringBackgroundDrag = true;
        try
        {
            requestWindowState(WindowState.Normal);
            if (maximized && !restored.IsEmpty) moveRestoredWindow(restored);
        }
        finally { restoringBackgroundDrag = false; }
        if (isLeftButtonPressed()) startDrag();
    }

    internal bool HandleBackgroundMouseMove(bool leftPressed, Point cursorPixels, Rect currentPixels,
        double dpiX, double dpiY)
    {
        if (backgroundDragOrigin is not { } origin) return false;
        if (!leftPressed || !CanDragBackground || WindowState != WindowState.Maximized || dpiX <= 0 || dpiY <= 0)
        {
            CancelBackgroundDrag();
            return false;
        }
        if (Math.Abs(cursorPixels.X - origin.X) / dpiX <= SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(cursorPixels.Y - origin.Y) / dpiY <= SystemParameters.MinimumVerticalDragDistance) return false;
        CancelBackgroundDrag();
        BeginBackgroundDrag(cursorPixels, currentPixels, dpiX, dpiY);
        return true;
    }

    internal void CancelBackgroundDrag()
    {
        backgroundDragOrigin = null;
        if (ShellRoot is not null && ReferenceEquals(Mouse.Captured, ShellRoot)) ShellRoot.ReleaseMouseCapture();
    }

    private void OnBackgroundMouseMove(object sender, MouseEventArgs e)
    {
        if (backgroundDragOrigin is null) return;
        if (!nativeFrame.TryGetDragGeometry(out var cursor, out var bounds, out var dpiX, out var dpiY))
        {
            CancelBackgroundDrag();
            return;
        }
        if (HandleBackgroundMouseMove(e.LeftButton == MouseButtonState.Pressed, cursor, bounds, dpiX, dpiY))
            e.Handled = true;
    }

    private void OnBackgroundMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) CancelBackgroundDrag();
    }

    private void OnBackgroundLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ShellRoot)) CancelBackgroundDrag();
    }

    private void OnEnterThreadModal(object? sender, EventArgs e) => CancelBackgroundDrag();

    private async void OnGoBackClick(object sender, RoutedEventArgs e) => await ReturnHomeAsync();

    private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (ComponentDispatcher.IsThreadModal || !HandleWorkstationInput(IsActive, e.StagingItem.Input, Keyboard.Modifiers)) return;
        e.StagingItem.Input.Handled = true;
        e.Cancel();
    }

    internal bool HandleWorkstationInput(bool isActive, InputEventArgs input, ModifierKeys modifiers)
    {
        if (input.Handled) return false;
        var origin = input.OriginalSource as DependencyObject;
        if (input is KeyEventArgs key && (input.RoutedEvent == Keyboard.KeyDownEvent || input.RoutedEvent == Keyboard.PreviewKeyDownEvent))
            return HandleWindowKeyDown(isActive, key.Key == Key.System ? key.SystemKey : key.Key, modifiers,
                key.IsRepeat, origin ?? Keyboard.FocusedElement as DependencyObject);
        if (input is MouseWheelEventArgs wheel && (input.RoutedEvent == Mouse.MouseWheelEvent || input.RoutedEvent == Mouse.PreviewMouseWheelEvent))
            return HandleWindowMouseWheel(isActive, modifiers, wheel.Delta, origin ?? Mouse.DirectlyOver as DependencyObject);
        return false;
    }

    internal bool HandleWindowKeyDown(bool isActive, Key key, ModifierKeys modifiers, bool isRepeat,
        DependencyObject? origin)
    {
        if (!isActive || IsPopupOverlayVisible || IsBlockingDrawerVisible || PageHost.Content is not WorkstationPage workstationPage) return false;
        if (IsDrawerOrigin(origin))
        {
            if (SampleManagerHost.Content is OHsInstrumentalArrangementDrawer piano)
                return HandleInstrumentalDrawerKey(piano, workstationPage, key, modifiers, isRepeat, origin);
            if (SampleManagerHost.Content is OHsKeysMappingDrawer && key == Key.F2 && modifiers == ModifierKeys.None && !OHsInstrumentalArrangementDrawer.IsTextInput(origin) && workstationPage.DataContext is WorkstationPageVM vm)
            { if (!isRepeat && vm.ManageSamplesCommand.CanExecute(null)) DrawerInputCompletion = ObserveDrawerInputAsync(vm.ManageSamplesCommand.ExecuteAsync(null)); return true; }
            return false;
        }

        return workstationPage.HandleVolumeShortcut(key, modifiers, origin) ||
               workstationPage.HandlePlaybackKey(key, modifiers, isRepeat, origin);
    }

    internal Task DrawerInputCompletion { get; private set; } = Task.CompletedTask;
    private async Task ObserveDrawerInputAsync(Task operation)
    {
        try { await operation; }
        catch (OperationCanceledException) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Drawer input failed: {0}", error); }
    }
    private bool HandleInstrumentalDrawerKey(OHsInstrumentalArrangementDrawer drawer, WorkstationPage page,
        Key key, ModifierKeys modifiers, bool isRepeat, DependencyObject? origin)
    {
        if (OHsInstrumentalArrangementDrawer.IsTextInput(origin)) return false;
        if (drawer.HandleEditorShortcut(key, modifiers, isRepeat, origin)) return true;
        if (page.DataContext is not WorkstationPageVM vm) return false;
        if (key == Key.F3 && modifiers == ModifierKeys.None)
        { if (!isRepeat && vm.CanPlay) vm.ScrollWithMarker = !vm.ScrollWithMarker; return true; }
        IAsyncRelayCommand? command = (key, modifiers) switch
        {
            (Key.Space, ModifierKeys.None) => vm.PlayPauseCommand,
            (Key.Space, ModifierKeys.Shift) => vm.StopCommand,
            (Key.Left, ModifierKeys.Control) => vm.PreviousWholeBeatCommand,
            (Key.Right, ModifierKeys.Control) => vm.NextWholeBeatCommand,
            (Key.Home or Key.End, ModifierKeys.Control) => vm.SeekCommand,
            (Key.F2, ModifierKeys.None) => vm.ManageSamplesCommand,
            _ => null
        };
        if (command is null) return false;
        object? parameter = key is Key.Home or Key.End ? (key == Key.Home ? TimeSpan.Zero : vm.Waveform?.Duration ?? TimeSpan.Zero) : null;
        if (!isRepeat && command.CanExecute(parameter)) DrawerInputCompletion = ObserveDrawerInputAsync(command.ExecuteAsync(parameter));
        return true;
    }

    internal bool HandleWindowMouseWheel(bool isActive, ModifierKeys modifiers, int delta,
        DependencyObject? origin = null)
    {
        if (!isActive || IsPopupOverlayVisible || IsBlockingDrawerVisible || IsDrawerOrigin(origin) || PageHost.Content is not WorkstationPage workstationPage) return false;

        return workstationPage.HandleContextWheel(modifiers, delta, origin);
    }

    internal void OnWindowDeactivated(object? sender, EventArgs e)
    {
        CancelBackgroundDrag();
        if (PageHost.Content is WorkstationPage workstation) workstation.DismissVolumeOverlay();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => requestWindowState(WindowState.Minimized);

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) => ToggleMaximizeRestore();

    private void ToggleMaximizeRestore()
    {
        if (WindowState == WindowState.Maximized)
            requestWindowState(WindowState.Normal);
        else requestWindowState(WindowState.Maximized);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void UpdateMaximizeRestoreButton()
    {
        if (!IsInitialized) return;
        ShellHeader.IsMaximized = WindowState == WindowState.Maximized;
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (viewModel.IsInteractionBlocked || viewModel.IsProcessing) CancelBackgroundDrag();
        if (e.PropertyName != nameof(HomePageVM.IsInteractionBlocked) || closed) return;
        if (viewModel.IsInteractionBlocked)
        {
            priorFocus = Keyboard.FocusedElement;
            Keyboard.ClearFocus();
        }
        else
        {
            focusRestore = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!closed && viewModel.CanInteract && PageHost.Content == page &&
                    priorFocus is FrameworkElement { IsVisible: true, IsEnabled: true } element &&
                    page.IsAncestorOf(element))
                    Keyboard.Focus(element);
                priorFocus = null;
            }));
        }
    }

    private void OnProcessingVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (ProcessingText.IsVisible)
            (UIElementAutomationPeer.FromElement(ProcessingText) ??
             UIElementAutomationPeer.CreatePeerForElement(ProcessingText))
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
