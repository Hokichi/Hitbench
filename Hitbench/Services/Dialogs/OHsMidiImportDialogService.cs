using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Hitbench.Core.Entities;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Helpers;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
using Microsoft.Win32;

namespace Hitbench.Services.Dialogs;

public sealed class OHsMidiImportDialogService(Func<Window?> getOwner, Dispatcher dispatcher,
    Func<OHsMidiImportSource, IReadOnlyList<UninheritedPoint>, OHsMidiImportPopupVM> create,
    Func<CancellationToken, Task<string?>>? picker = null,
    Func<OHsMidiImportPopupVM, CancellationToken, Task<bool>>? present = null,
    Func<string, string, bool, CancellationToken, Task<bool>>? message = null) : IOHsMidiImportDialogService
{
    private async Task<T> OnUiAsync<T>(Func<Task<T>> action, CancellationToken token) =>
        await dispatcher.InvokeAsync(action, DispatcherPriority.Normal, token).Task.Unwrap();

    public Task<string?> PickFileAsync(CancellationToken token) => OnUiAsync(async () =>
    {
        token.ThrowIfCancellationRequested();
        if (picker is not null) { var path = await picker(token); token.ThrowIfCancellationRequested(); return path; }
        var owner = getOwner();
        if (owner?.IsVisible != true) return null;
        var dialog = new OpenFileDialog { Title = "Import MIDI", Filter = "MIDI files (*.mid;*.midi)|*.mid;*.midi", Multiselect = false, CheckFileExists = true };
        var accepted = dialog.ShowDialog(owner) == true;
        token.ThrowIfCancellationRequested(); return accepted ? dialog.FileName : null;
    }, token);

    public Task<OHsMidiImportMode?> ChooseTimingAsync(OHsMidiImportSource source, IReadOnlyList<UninheritedPoint> points, CancellationToken token) =>
        OnUiAsync<OHsMidiImportMode?>(async () =>
        {
            token.ThrowIfCancellationRequested();
            var vm = create(source, points);
            bool accepted;
            if (present is not null) accepted = await present(vm, token);
            else
            {
                var owner = getOwner();
                if (owner?.IsVisible != true) return null;
                var popup = new OHsMidiImportPopup(vm) { Owner = owner, FlowDirection = owner.FlowDirection };
                KeyboardNavigationPolicy.SetSuppressTabNavigation(popup, true);
                using var cancel = token.Register(() => dispatcher.BeginInvoke(() => { if (popup.IsVisible) popup.TryComplete(false); }));
                token.ThrowIfCancellationRequested(); accepted = popup.ShowDialog() == true;
            }
            token.ThrowIfCancellationRequested(); return accepted && vm.CanImport ? vm.SelectedMode : null;
        }, token);

    public async Task<bool> ConfirmExclusionsAsync(OHsMidiImportSource source, CancellationToken token) =>
        await ShowMessageAsync("Import MIDI", string.Format(CultureInfo.CurrentCulture,
            "Import will exclude {0} notes below C1 and {1} percussion parts.\n{2}", source.ExcludedPitchCount,
            source.ExcludedPercussionParts.Count, string.Join(Environment.NewLine, source.ExcludedPercussionParts)), true, token);

    public async Task ShowErrorAsync(string title, string text, CancellationToken token) =>
        await ShowMessageAsync(title, text, false, token);

    private Task<bool> ShowMessageAsync(string title, string text, bool confirm, CancellationToken token) => OnUiAsync(async () =>
    {
        token.ThrowIfCancellationRequested();
        if (message is not null) { var accepted = await message(title, text, confirm, token); token.ThrowIfCancellationRequested(); return accepted; }
        var owner = getOwner();
        if (owner?.IsVisible != true) return false;
        var popup = new OHsMessageBoxPopup(text, title, confirm ? MessageBoxButton.OKCancel : MessageBoxButton.OK,
            confirm ? MessageBoxImage.Warning : MessageBoxImage.Error) { Owner = owner, FlowDirection = owner.FlowDirection };
        KeyboardNavigationPolicy.SetSuppressTabNavigation(popup, true);
        using var cancel = token.Register(() => dispatcher.BeginInvoke(() => { if (popup.IsVisible) popup.Close(); }));
        token.ThrowIfCancellationRequested(); popup.ShowDialog(); token.ThrowIfCancellationRequested();
        return popup.Result == MessageBoxResult.OK;
    }, token);
}
