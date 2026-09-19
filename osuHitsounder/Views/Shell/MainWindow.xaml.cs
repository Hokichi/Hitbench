using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.Ui;
using osuHitsounder.Views.Pages;
using osuHitsounder.ViewModels.Shell.HomePage;

namespace osuHitsounder;

public partial class MainWindow : Window, IRecipient<ProjectCreatedMessage>,
    IRecipient<ProjectOpenRequestedMessage>, IRecipient<ProjectCreationFailedMessage>
{
    private readonly HomePage page;
    private readonly IMessenger messenger;
    private readonly Func<WorkstationPage> createWorkstation;
    private readonly CancellationTokenSource lifetime = new();
    private Task? loading;
    private bool closed;
    private readonly HomePageVM viewModel;
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

    public MainWindow(HomePage page, IMessenger messenger, Func<WorkstationPage> createWorkstation,
        HomePageVM viewModel, IAppSettingsService settings)
        : this(page, messenger, createWorkstation, viewModel, settings, null) { }

    internal MainWindow(HomePage page, IMessenger messenger, Func<WorkstationPage> createWorkstation,
        HomePageVM viewModel, IAppSettingsService settings, Action<string>? reportError,
        Action<WindowState>? requestWindowState = null, Func<Window, Size>? getWorkArea = null)
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
        this.settings = settings;
        this.getWorkArea = getWorkArea ?? WindowWorkArea.GetClientSize;
        InitializeComponent();
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
        LayoutUpdated += OnLayoutEnvironmentChanged;
        DpiChanged += OnWindowDpiChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    public string? FatalError => page.FatalError;
    public bool RequiresShutdown => page.RequiresShutdown;

    public Task LoadAsync() => loading ??= LoadPageAsync();

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
        if (closed || PageHost.Content != page)
            return;
        try
        {
            var workstation = createWorkstation();
            workstation.SetProjectPath(projectPath);
            page.Deactivate();
            PageHost.Content = workstation;
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

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        placementTimer.Stop();
        placementTimer.Tick -= OnPlacementTimerTick;
        SizeChanged -= OnWindowSizeChanged;
        StateChanged -= OnWindowStateChanged;
        LocationChanged -= OnLayoutEnvironmentChanged;
        SourceInitialized -= OnLayoutEnvironmentChanged;
        LayoutUpdated -= OnLayoutEnvironmentChanged;
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
        if (e.Cancel || closingAfterSave || (!placementDirty && placementSave.IsCompleted)) return;
        e.Cancel = true;
        if (closingForSave) return;
        closingForSave = true;
        placementTimer.Stop();
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
    private void OnWindowDpiChanged(object sender, DpiChangedEventArgs e) => QueueSongsLayout();
    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e) => QueueSongsLayout();

    private void QueueSongsLayout()
    {
        if (closed || PageHost.Content != page || layoutRefresh?.Status == DispatcherOperationStatus.Pending) return;
        layoutRefresh = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (closed || PageHost.Content != page || page.ActualWidth <= 0) return;
            var size = getWorkArea(this);
            if (size.IsEmpty || size.Width <= 0 || size.Height <= 0) return;
            // Named shell content uses a border and a header row. Measure their public layout values.
            var root = ShellRoot;
            var grid = (Grid)root.Child;
            var header = grid.RowDefinitions[0].ActualHeight;
            page.UpdateSongsLayout(new Size(size.Width, Math.Max(0, size.Height - header)));
            var width = page.MinWidth + root.BorderThickness.Left + root.BorderThickness.Right;
            var height = page.MinHeight + header + root.BorderThickness.Top + root.BorderThickness.Bottom;
            if (Math.Abs(MinWidth - width) > .01) MinWidth = width;
            if (Math.Abs(MinHeight - height) > .01) MinHeight = height;
        }));
    }

    internal void RecordWindowSize(double width, double height)
    {
        if (!placementInitialized || closed || WindowState != WindowState.Normal) return;
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
        if (!lastIsMaximized)
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

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }
        if (WindowState == WindowState.Normal) DragMove();
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
        var restoring = WindowState == WindowState.Maximized;
        var label = restoring ? "Restore" : "Maximize";
        MaximizeRestoreButton.ButtonIcon = (Geometry)FindResource(restoring ? "CompressAlt" : "ExpandAlt");
        MaximizeRestoreButton.ButtonText = label;
        AutomationProperties.SetName(MaximizeRestoreButton, label);
        MaximizeRestoreButton.ToolTip = label;
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
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
