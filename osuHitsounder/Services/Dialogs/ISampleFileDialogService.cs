namespace osuHitsounder.Services.Dialogs;

public interface ISampleFileDialogService
{
    Task<string?> SelectAsync(CancellationToken token);
}
