using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Onset.Resources.CustomControls;

namespace Onset.Services.Dialogs;

public sealed class ProjectRemovalDialogService(Func<Window?> getOwner, Dispatcher dispatcher) : IProjectRemovalDialogService
{
    public async Task<bool> ConfirmAsync(string projectName, CancellationToken token) => await dispatcher.InvokeAsync(() =>
    {
        token.ThrowIfCancellationRequested();
        var owner = getOwner();
        if (owner?.IsVisible != true) return false;
        var popup = new OHsMessageBoxPopup(string.Format(CultureInfo.CurrentCulture,
            "Delete project '{0}' and all copied samples in its project folder? This cannot be undone. The original mapset will remain unchanged.", projectName),
            "Delete project?", MessageBoxButton.OKCancel, MessageBoxImage.Warning, true,
            "Delete Project", "Delete project", "Delete") { Owner = owner };
        using var registration = token.Register(() => dispatcher.BeginInvoke(new Action(() =>
        {
            if (popup.IsVisible) popup.Close();
        })));
        popup.ShowDialog();
        token.ThrowIfCancellationRequested();
        return owner.IsVisible && popup.Result == MessageBoxResult.OK;
    }, DispatcherPriority.Normal, token);
}
