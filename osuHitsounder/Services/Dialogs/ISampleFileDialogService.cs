namespace osuHitsounder.Services.Dialogs;

public interface ISampleFileDialogService
{
    Task<IReadOnlyList<string>> SelectAsync(osuHitsounder.Resources.DataModels.SamplePickerContext context, CancellationToken token);
    Task<IReadOnlyList<string>> SelectAsync(string? initialDirectory, CancellationToken token, osuHitsounder.Resources.DataModels.SampleSelectionMode mode=osuHitsounder.Resources.DataModels.SampleSelectionMode.Multiple);
}
