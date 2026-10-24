using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.Services.Ui;
using Hitbench.Services.FileSystem;
using Hitbench.Services.Dialogs;
using Hitbench.ViewModels.Popups;

namespace Hitbench;

public partial class App
{
    private static Func<InstrumentalEditorContext, InstrumentalArrangementDrawerVM> CreateInstrumentEditorFactory(IServiceProvider provider) =>
        context => new InstrumentalArrangementDrawerVM(context,
                provider.GetRequiredService<IMessenger>(),provider.GetRequiredService<IUiScheduler>());
}
