namespace osuHitsounder.Services.Dialogs;

public interface ISampleFileDialogService
{
    Task<IReadOnlyList<string>> SelectAsync(string? initialDirectory, CancellationToken token, osuHitsounder.Resources.DataModels.SampleSelectionMode mode=osuHitsounder.Resources.DataModels.SampleSelectionMode.Multiple);
}
