using System.Windows;
using System.Windows.Threading;
using Soundie.Resources.DataModels;
using Soundie.ViewModels.Popups;
using Soundie.Views.Popups;
namespace Soundie.Services.Dialogs;

public sealed class InstrumentalTrackDialogService(Func<Window?> getOwner, Dispatcher dispatcher,
 Func<InstrumentalEditorContext, InstrumentalArrangementDrawerVM> createArrangement,
 Func<InstrumentalTrackSnapshot, KeysMappingDrawerVM> createMapping,
 Func<InstrumentalArrangementDrawerVM, CancellationToken, Task>? presentArrangement = null,
 Func<KeysMappingDrawerVM, CancellationToken, Task>? presentMapping = null) : IInstrumentalTrackDialogService
{
    public async Task ShowArrangementAsync(InstrumentalEditorContext context, CancellationToken token) =>
     await dispatcher.InvokeAsync(async () =>
     {
         if (token.IsCancellationRequested) return;
         var owner = getOwner(); if (presentArrangement is null && (owner is not MainWindow || !owner.IsVisible)) return;
         var vm = createArrangement(context);
         try
         {
             await vm.InitializeAsync(token); if (token.IsCancellationRequested) return;
             if (presentArrangement is not null) await presentArrangement(vm, token);
             else
             {
                 await ((MainWindow)owner!).PresentDrawerAsync(new OHsInstrumentalArrangementDrawer(vm), token, bottomNonModal: true);
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
         try { if (presentMapping is not null) await presentMapping(vm, token); else await ((MainWindow)owner!).PresentDrawerAsync(new OHsKeysMappingDrawer(vm), token, bottomNonModal: true); }
         finally { await vm.DeactivateAsync(); }
     }).Task.Unwrap();
}
