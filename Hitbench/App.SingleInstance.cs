using System.Diagnostics;
using System.Windows;
using Hitbench.Services.Ui;

namespace Hitbench;

public partial class App
{
    private OHsSingleInstanceCoordinator? instanceCoordinator;
    private OHsInstanceActivationGate? activationGate;
    private MainWindow? activeShell;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        activationGate = new(new UiScheduler(Dispatcher), () => activeShell?.RestoreAndActivate());
        instanceCoordinator = new();
        try
        {
            if (!await instanceCoordinator.RunPrimaryOrActivateAsync(StartPrimaryAsync, activationGate.Request, default))
                Shutdown();
        }
        catch (Exception error)
        {
            Trace.TraceError("Instance startup: {0}", error);
            Shutdown(1);
        }
    }
}
