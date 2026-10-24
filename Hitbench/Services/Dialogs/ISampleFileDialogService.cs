namespace Hitbench.Services.Dialogs;

public interface ISampleFileDialogService
{
    Task<IReadOnlyList<string>> SelectAsync(Hitbench.Resources.DataModels.SamplePickerContext context, CancellationToken token);
    Task<IReadOnlyList<string>> SelectAsync(string? initialDirectory, CancellationToken token, Hitbench.Resources.DataModels.SampleSelectionMode mode=Hitbench.Resources.DataModels.SampleSelectionMode.Multiple);
}
