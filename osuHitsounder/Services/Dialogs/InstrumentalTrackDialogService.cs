using System.Windows;
using System.Windows.Threading;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;
namespace osuHitsounder.Services.Dialogs;

public sealed class InstrumentalTrackDialogService(Func<Window?> getOwner, Dispatcher dispatcher,
 Func<InstrumentalEditorContext, InstrumentalArrangementPopupVM> createArrangement,
 Func<InstrumentalTrackSnapshot, KeysMappingDrawerVM> createMapping,
 Func<InstrumentalArrangementPopupVM, CancellationToken, Task>? presentArrangement = null,
 Func<KeysMappingDrawerVM, CancellationToken, Task>? presentMapping = null) : IInstrumentalTrackDialogService
{
    public async Task ShowArrangementAsync(InstrumentalEditorContext context, CancellationToken token) =>
     await dispatcher.InvokeAsync(async () =>
     {
         if (token.IsCancellationRequested) return;
         var owner = getOwner(); if (presentArrangement is null && (owner is null || !owner.IsVisible)) return;
         var vm = createArrangement(context);
         try
         {
             await vm.InitializeAsync(token); if (token.IsCancellationRequested) return;
             if (presentArrangement is not null) await presentArrangement(vm, token);
             else
             {
                 var popup = new OHsInstrumentalArrangementPopup(vm) { Owner = owner, FlowDirection = owner!.FlowDirection };
                 using var cancel = token.Register(() => dispatcher.BeginInvoke(() => { if (popup.IsVisible) popup.Close(); }));
                 popup.ShowDialog();
             }
         }
         finally { await vm.DeactivateAsync(); }
     }).Task.Unwrap();
    public async Task ShowKeysMappingAsync(InstrumentalTrackSnapshot snapshot, CancellationToken token) =>
     await dispatcher.InvokeAsync(async () =>
     {
         if (token.IsCancellationRequested) return;
         var owner = getOwner(); if (presentMapping is null && (owner is not MainWindow || !owner.IsVisible)) return;
         var vm = createMapping(snapshot);
         try { if (presentMapping is not null) await presentMapping(vm, token); else await ((MainWindow)owner!).PresentDrawerAsync(new OHsKeysMappingDrawer(vm), token); }
         finally { await vm.DeactivateAsync(); }
     }).Task.Unwrap();
}
