namespace osuHitsounder.Services.Dialogs;

public interface ISampleFileDialogService
{
    Task<IReadOnlyList<string>> SelectAsync(CancellationToken token);
}
