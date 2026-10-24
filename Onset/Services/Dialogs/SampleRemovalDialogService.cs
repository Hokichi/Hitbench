using System.Windows;
using System.Windows.Threading;
using Onset.Resources.CustomControls;

namespace Onset.Services.Dialogs;

public sealed class SampleRemovalDialogService : ISampleRemovalDialogService
{
    private readonly Func<Window?> getOwner;
    private readonly Dispatcher dispatcher;
    private readonly Func<OHsMessageBoxPopup, CancellationToken, Task<bool>>? confirm;
    public SampleRemovalDialogService(Func<Window?> getOwner, Dispatcher dispatcher) : this(getOwner, dispatcher, null) { }
    internal SampleRemovalDialogService(Func<Window?> getOwner, Dispatcher dispatcher,
        Func<OHsMessageBoxPopup, CancellationToken, Task<bool>>? confirm)
    { this.getOwner = getOwner; this.dispatcher = dispatcher; this.confirm = confirm; }

    public async Task<bool> ConfirmAsync(string fileName, CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;
        return await dispatcher.InvokeAsync(async () =>
        {
            if (token.IsCancellationRequested) return false;
            var owner = getOwner();
            if (confirm is null && owner?.IsVisible != true) return false;
            var popup = new OHsMessageBoxPopup(string.Format(System.Globalization.CultureInfo.CurrentCulture,
                "Delete '{0}' from this project's Samples folder? This permanently deletes the project copy. Track placements will remain, and tracks using this sample will be unavailable until you select another sample.", fileName),
                "Delete sample?", MessageBoxButton.OKCancel, MessageBoxImage.Warning, true,
                "Delete Sample", "Delete sample", "Delete");
            try
            {
                if (confirm is not null) return await confirm(popup, token) && !token.IsCancellationRequested;
                popup.Owner = owner; popup.FlowDirection = owner!.FlowDirection;
                using var registration = token.Register(() => dispatcher.BeginInvoke(new Action(() => { if (popup.IsVisible) popup.Close(); })));
                popup.ShowDialog();
                return !token.IsCancellationRequested && owner.IsVisible && popup.Result == MessageBoxResult.OK;
            }
            finally { popup.Close(); }
        }).Task.Unwrap();
    }
}
