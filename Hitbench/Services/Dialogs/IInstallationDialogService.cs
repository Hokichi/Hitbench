namespace Hitbench.Services.Dialogs;

public interface IInstallationDialogService
{
    Task<string?> LocateAfterScanAsync(CancellationToken cancellationToken);
    Task ShowNotFoundAsync(CancellationToken cancellationToken);
}

