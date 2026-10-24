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
    private static Func<InstrumentalEditorContext, InstrumentalArrangementDrawerVM> CreateInstrumentEditorFactory(IServiceProvider provider) =>
        context => new InstrumentalArrangementDrawerVM(context,
                provider.GetRequiredService<IMessenger>(),provider.GetRequiredService<IUiScheduler>());
}
