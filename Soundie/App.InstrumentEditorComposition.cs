using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Soundie.Resources.DataModels;
using Soundie.Services;
using Soundie.Services.Ui;
using Soundie.Services.FileSystem;
using Soundie.Services.Dialogs;
using Soundie.ViewModels.Popups;

namespace Soundie;

public partial class App
{
    private static Func<InstrumentalEditorContext, InstrumentalArrangementDrawerVM> CreateInstrumentEditorFactory(IServiceProvider provider) =>
        context => new InstrumentalArrangementDrawerVM(context,
                provider.GetRequiredService<IMessenger>(),provider.GetRequiredService<IUiScheduler>());
}
