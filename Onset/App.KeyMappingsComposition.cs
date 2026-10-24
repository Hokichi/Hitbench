using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Onset.Resources.DataModels;
using Onset.Services;
using Onset.Services.Ui;
using Onset.Services.FileSystem;
using Onset.Services.Dialogs;
using Onset.ViewModels.Popups;

namespace Onset;

public partial class App
{
    private static async Task<bool> ConfirmClearMappingsAsync(string trackName, CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;
        return await Current.Dispatcher.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested || Current.MainWindow is not { IsVisible: true } owner) return false;
            var popup = new Onset.Resources.CustomControls.OHsMessageBoxPopup(
                string.Format(System.Globalization.CultureInfo.CurrentCulture,"Clear all key mappings for '{0}'? Sample files, sample folders, and instrumental notes will remain.",trackName),
                "Clear all mappings?", System.Windows.MessageBoxButton.OKCancel,System.Windows.MessageBoxImage.Warning,true,"Clear All Mappings","Clear all key mappings","Delete") { Owner = owner, FlowDirection = owner.FlowDirection };
            using var registration = token.Register(() => Current.Dispatcher.BeginInvoke(new Action(() => { if (popup.IsVisible) popup.Close(); })));
            try { popup.ShowDialog(); return !token.IsCancellationRequested && owner.IsVisible && popup.Result == System.Windows.MessageBoxResult.OK; }
            finally { popup.Close(); }
        });
    }
    private static Func<InstrumentalTrackSnapshot, KeysMappingDrawerVM> CreateKeyMappingsFactory(IServiceProvider provider) =>
        snapshot => new KeysMappingDrawerVM(snapshot,provider.GetRequiredService<IMessenger>(),provider.GetRequiredService<IUiScheduler>(),
                provider.GetRequiredService<ISampleFileService>(),provider.GetRequiredService<ISampleFileDialogService>(),provider.GetRequiredService<IMappingConflictDialogService>(), new SamplePreviewService(AudioDecoder.Open, new AudioOutputFactory().CreateCandidates(null), provider.GetRequiredService<Onset.Services.Ui.OHsAudioOperationProgress>()), provider.GetRequiredService<PlaybackVolumeSettings>(), ConfirmClearMappingsAsync);
}
