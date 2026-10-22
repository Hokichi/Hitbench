namespace Soundie.Services.Dialogs;

public interface ISampleFileDialogService
{
    Task<IReadOnlyList<string>> SelectAsync(Soundie.Resources.DataModels.SamplePickerContext context, CancellationToken token);
    Task<IReadOnlyList<string>> SelectAsync(string? initialDirectory, CancellationToken token, Soundie.Resources.DataModels.SampleSelectionMode mode=Soundie.Resources.DataModels.SampleSelectionMode.Multiple);
}
