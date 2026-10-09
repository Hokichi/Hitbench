namespace osuHitsounder.Services.Dialogs;

public interface ISampleFileDialogService
{
    Task<IReadOnlyList<string>> SelectAsync(string? initialDirectory, CancellationToken token);
}
