using System.Windows;
using System.Windows.Threading;
using Onset.Resources.DataModels;
using Onset.ViewModels.Popups;
using Onset.Views.Popups;

namespace Onset.Services.Dialogs;

public sealed class MappingConflictDialogService(Func<Window?> getOwner, Dispatcher dispatcher,
    Func<MappingConflictsPopupVM, CancellationToken, Task<IReadOnlyDictionary<string, string>?>>? present = null) : IMappingConflictDialogService
{
    public async Task<IReadOnlyDictionary<string, string>?> SelectAsync(IReadOnlyList<MappingConflict> conflicts, CancellationToken token) =>
        await dispatcher.InvokeAsync(async () =>
        {
            if (token.IsCancellationRequested) return null;
            var vm = new MappingConflictsPopupVM(conflicts);
            if (present is not null)
            {
                var choices = await present(vm, token);
                return token.IsCancellationRequested ? null : choices is null ? null :
                    (IReadOnlyDictionary<string, string>)new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(choices));
            }
            var owner = getOwner(); if (owner?.IsVisible != true) return null;
            var popup = new OHsMappingConflictsPopup(vm) { Owner = owner, FlowDirection = owner.FlowDirection };
            using var cancellation = token.Register(() => dispatcher.BeginInvoke(new Action(() => { if (popup.IsVisible) popup.Close(); })));
            popup.ShowDialog();
            return token.IsCancellationRequested || !owner.IsVisible ? null : popup.Result;
        }).Task.Unwrap();
}
