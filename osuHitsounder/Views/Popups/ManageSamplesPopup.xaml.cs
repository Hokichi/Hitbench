using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.Helpers;
using osuHitsounder.Services.Ui;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Views.Popups;

public partial class ManageSamplesPopup : BasePopup
{
    private readonly ManageSamplesPopupVM viewModel;
    private bool closing;
    private Window? sizingOwner;
    public ManageSamplesPopup(ManageSamplesPopupVM viewModel)
    {
        InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
    }
    protected override bool ShouldBubbleApplicationClose => false;
    public async Task InitializeAsync(CancellationToken token)
    {
        sizingOwner = Owner;
        if (sizingOwner is not null) sizingOwner.SizeChanged += OnOwnerSizeChanged;
        DpiChanged += OnDpiChanged; UpdateSize(); await viewModel.InitializeAsync(token);
    }
    private void UpdateSize()
    {
        if (sizingOwner is null) return;
        var area = WindowWorkArea.GetClientSize(sizingOwner);
        Width = Math.Max(1, Math.Min(720, Math.Min(area.Width, sizingOwner.ActualWidth) - 32));
        Height = Math.Max(1, Math.Min(480, Math.Min(area.Height, sizingOwner.ActualHeight) - 32));
    }
    private void OnOwnerSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSize();
    private void OnDpiChanged(object sender, DpiChangedEventArgs e) => UpdateSize();
    public async Task DeactivateAsync()
    {
        DpiChanged -= OnDpiChanged;
        if (sizingOwner is not null) sizingOwner.SizeChanged -= OnOwnerSizeChanged;
        await viewModel.DeactivateAsync();
    }
    protected override async void OnCloseButtonClick()
    {
        if (closing) return; closing = true;
        try { if (await viewModel.PrepareToCloseAsync()) Close(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Sample manager close failed: {0}", error); }
        finally { closing = false; }
    }
    private void OnEditorVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox input && input.IsVisible)
            Dispatcher.BeginInvoke(new Action(() => { if (input.IsVisible) { input.Focus(); input.SelectAll(); } }));
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && viewModel.ActiveEdit is not null && !viewModel.IsBusy)
        { e.Handled = true; viewModel.CancelEditCommand.Execute(null); AddNewSampleButton.Focus(); return; }
        base.OnPreviewKeyDown(e);
    }
    private async void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return; e.Handled = true;
        try { await viewModel.CommitRenameCommand.ExecuteAsync(null); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Sample edit failed: {0}", error); }
    }
    private async void OnEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        try { if (viewModel.ActiveEdit is not null) await viewModel.CommitRenameCommand.ExecuteAsync(null); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Sample edit failed: {0}", error); }
    }
}

