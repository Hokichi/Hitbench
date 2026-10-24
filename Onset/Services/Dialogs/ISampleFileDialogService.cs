namespace Onset.Services.Dialogs;

public interface ISampleFileDialogService
{
    Task<IReadOnlyList<string>> SelectAsync(Onset.Resources.DataModels.SamplePickerContext context, CancellationToken token);
    Task<IReadOnlyList<string>> SelectAsync(string? initialDirectory, CancellationToken token, Onset.Resources.DataModels.SampleSelectionMode mode=Onset.Resources.DataModels.SampleSelectionMode.Multiple);
}
