using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Hitbench.Resources.CustomControls;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Views.Popups;

public partial class OHsUpdatePopup : BasePopup
{
    private readonly OHsUpdatePopupVM viewModel;
    private readonly Action complete;
    private bool completing;
    public OHsUpdatePopup(OHsUpdatePopupVM viewModel) : this(viewModel, null) { }
    internal OHsUpdatePopup(OHsUpdatePopupVM viewModel, Action? complete)
    {
        this.viewModel = viewModel; this.complete = complete ?? Close;
        InitializeComponent(); DataContext = viewModel;
        viewModel.Completed += OnCompleted;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }
    protected override bool ShouldBubbleApplicationClose => false;
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try { await viewModel.DownloadAsync(default); }
        catch (Exception error) { Trace.TraceError("Update popup: {0}", error); viewModel.RequestClose(); }
    }
    private void OnCompleted() { completing = true; complete(); }
    protected override void OnCloseButtonClick() => viewModel.RequestClose();
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!completing) { e.Cancel = true; viewModel.RequestClose(); }
        base.OnClosing(e);
    }
    private void OnClosed(object? sender, EventArgs e) => Detach();
    internal void Detach() { viewModel.Completed -= OnCompleted; Loaded -= OnLoaded; Closed -= OnClosed; }
}
