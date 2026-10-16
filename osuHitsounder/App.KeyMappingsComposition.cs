using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.Services.Ui;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.Dialogs;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder;

public partial class App
{
    private static Func<InstrumentalTrackSnapshot, KeysMappingDrawerVM> CreateKeyMappingsFactory(IServiceProvider provider) =>
        snapshot => new KeysMappingDrawerVM(snapshot,provider.GetRequiredService<IMessenger>(),provider.GetRequiredService<IUiScheduler>(),
                provider.GetRequiredService<ISampleFileService>(),provider.GetRequiredService<ISampleFileDialogService>(),provider.GetRequiredService<IMappingConflictDialogService>());
}
