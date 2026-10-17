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
    private static Func<InstrumentalEditorContext, InstrumentalArrangementDrawerVM> CreateInstrumentEditorFactory(IServiceProvider provider) =>
        context => new InstrumentalArrangementDrawerVM(context,
                provider.GetRequiredService<IMessenger>(),provider.GetRequiredService<IUiScheduler>());
}
