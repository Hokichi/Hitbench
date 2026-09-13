using System.Windows;
using System.Windows.Threading;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Services.Dialogs;

public sealed class ProjectCreationDialogService(
    Func<Window?> getOwner,
    Dispatcher dispatcher,
    Func<string, IReadOnlySet<string>, ProjectNamePopupVM> createViewModel)
    : IProjectCreationDialogService
{
    public async Task<string?> RequestNameAsync(
        string suggestedName,
        IReadOnlySet<string> existingNames,
        CancellationToken cancellationToken)
    {
        return await dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var viewModel = createViewModel(suggestedName, existingNames);
            var popup = new ProjectNamePopup(viewModel);
            var owner = getOwner();
            if (owner?.IsVisible == true) popup.Owner = owner;
            using var registration = cancellationToken.Register(() => dispatcher.BeginInvoke(() =>
            {
                if (popup.IsVisible) popup.DialogResult = false;
            }));
            var confirmed = popup.ShowDialog() == true;
            cancellationToken.ThrowIfCancellationRequested();
            return confirmed ? viewModel.EffectiveName : null;
        }, DispatcherPriority.Normal, cancellationToken);
    }
}
