using System.Windows;
using System.Windows.Threading;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Services.Dialogs;

public sealed class SampleFileDialogService : ISampleFileDialogService
{
    private readonly Func<Window?> getOwner;
    private readonly Dispatcher dispatcher;
    private readonly Func<SampleLoaderPopupVM> createViewModel;
    private readonly Func<SampleLoaderPopupVM, CancellationToken, Task<IReadOnlyList<string>>>? select;

    public SampleFileDialogService(Func<Window?> getOwner, Dispatcher dispatcher, Func<SampleLoaderPopupVM> createViewModel)
        : this(getOwner, dispatcher, createViewModel, null) { }

    internal SampleFileDialogService(Func<Window?> getOwner, Dispatcher dispatcher,
        Func<SampleLoaderPopupVM> createViewModel,
        Func<SampleLoaderPopupVM, CancellationToken, Task<IReadOnlyList<string>>>? select)
    {
        this.getOwner = getOwner;
        this.dispatcher = dispatcher;
        this.createViewModel = createViewModel;
        this.select = select;
    }

    public async Task<IReadOnlyList<string>> SelectAsync(string? initialDirectory, CancellationToken token, SampleSelectionMode mode=SampleSelectionMode.Multiple) =>
        await dispatcher.InvokeAsync(() => SelectCoreAsync(initialDirectory, token, mode)).Task.Unwrap();

    private async Task<IReadOnlyList<string>> SelectCoreAsync(string? initialDirectory, CancellationToken token, SampleSelectionMode mode)
    {
        if (token.IsCancellationRequested) return Array.Empty<string>();
        var owner = select is null ? getOwner() : null;
        if (select is null && (owner is null || !owner.IsVisible)) return Array.Empty<string>();
        var vm = createViewModel();
        vm.ConfigureSelectionMode(mode);
        SampleLoaderPopup? popup = null;
        try
        {
            IReadOnlyList<string> paths;
            if (select is not null)
            {
                await vm.InitializeAsync(initialDirectory, token);
                token.ThrowIfCancellationRequested();
                paths = await select(vm, token);
            }
            else
            {
                popup = new SampleLoaderPopup(vm) { Owner = owner, FlowDirection = owner!.FlowDirection };
                using var cancellation = token.Register(() => dispatcher.BeginInvoke(new Action(() =>
                {
                    if (popup.IsVisible) popup.Close();
                })));
                await popup.InitializeAsync(initialDirectory, token);
                token.ThrowIfCancellationRequested();
                paths = popup.ShowDialog() == true ? popup.Result : Array.Empty<string>();
            }
            token.ThrowIfCancellationRequested();
            if(mode != SampleSelectionMode.Multiple && paths.Count > 1) throw new InvalidOperationException("Select one sample or folder.");
            return Array.AsReadOnly(paths.ToArray());
        }
        catch (OperationCanceledException) { return Array.Empty<string>(); }
        finally
        {
            if (popup is not null) await popup.DeactivateAsync();
            else await vm.DeactivateAsync();
        }
    }
}
