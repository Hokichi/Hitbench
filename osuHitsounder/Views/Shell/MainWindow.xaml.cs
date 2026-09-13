using System.Windows;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.Messages;
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
    private IInputElement? priorFocus;
    private DispatcherOperation? focusRestore;

    public MainWindow(HomePage page, IMessenger messenger, Func<WorkstationPage> createWorkstation, HomePageVM viewModel)
    {
        this.page = page;
        this.viewModel = viewModel;
        this.messenger = messenger;
        this.createWorkstation = createWorkstation;
        InitializeComponent();
        DataContext = viewModel;
        PageHost.Content = page;
        viewModel.PropertyChanged += OnStateChanged;
        ProcessingText.IsVisibleChanged += OnProcessingVisibilityChanged;
        messenger.Register<ProjectCreatedMessage>(this);
        messenger.Register<ProjectCreationFailedMessage>(this);
    }

    public string? FatalError => page.FatalError;
    public bool RequiresShutdown => page.RequiresShutdown;

    public Task LoadAsync() => loading ??= LoadPageAsync();

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
        catch (OperationCanceledException) when (!closed && PageHost.Content != page)
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
