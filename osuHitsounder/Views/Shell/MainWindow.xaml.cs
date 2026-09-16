using System.Windows;
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
using osuHitsounder.Views.Pages;
using osuHitsounder.ViewModels.Shell.HomePage;

namespace osuHitsounder;

public partial class MainWindow : Window, IRecipient<ProjectCreatedMessage>, IRecipient<ProjectCreationFailedMessage>
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

    public MainWindow(HomePage page, IMessenger messenger, Func<WorkstationPage> createWorkstation,
        HomePageVM viewModel, IAppSettingsService settings)
    {
        this.page = page;
        this.viewModel = viewModel;
        this.messenger = messenger;
        this.createWorkstation = createWorkstation;
        this.settings = settings;
        InitializeComponent();
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
        messenger.Register<ProjectCreationFailedMessage>(this);
        SizeChanged += OnWindowSizeChanged;
        StateChanged += OnWindowStateChanged;
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

    public void Receive(ProjectCreatedMessage message)
    {
        if (closed || PageHost.Content != page)
            return;
        try
        {
            var workstation = createWorkstation();
            page.Deactivate();
            PageHost.Content = workstation;
        }
        catch (Exception error)
        {
            MessageBox.Show(this, $"Unable to open workstation: {error.Message}", "osuHitsounder",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Receive(ProjectCreationFailedMessage message)
    {
        if (!closed && PageHost.Content == page)
            MessageBox.Show(this, message.Error, "osuHitsounder", MessageBoxButton.OK, MessageBoxImage.Error);
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
        focusRestore?.Abort();
        viewModel.PropertyChanged -= OnStateChanged;
        ProcessingText.IsVisibleChanged -= OnProcessingVisibilityChanged;
        messenger.UnregisterAll(this);
        page.Deactivate();
        lifetime.Cancel();
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
        if (!placementInitialized || closed || WindowState != WindowState.Normal) return;
        restoredWidth = ActualWidth;
        restoredHeight = ActualHeight;
        QueuePlacementSave();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeRestoreButton();
        if (!placementInitialized || closed || WindowState == WindowState.Minimized) return;
        lastIsMaximized = WindowState == WindowState.Maximized;
        if (!lastIsMaximized)
        {
            restoredWidth = ActualWidth;
            restoredHeight = ActualHeight;
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

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) => ToggleMaximizeRestore();

    private void ToggleMaximizeRestore()
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
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
